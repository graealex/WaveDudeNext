// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WaveDudeNext.Runs;

/// <summary>
/// What kind of storage holds a path (2026-10-31, for the first start's choice of the preview quality): whether the
/// volume incurs a seek penalty, Windows' own distinction between a hard disk and a solid-state drive, the one its
/// drive optimiser goes by. Asked with <c>IOCTL_STORAGE_QUERY_PROPERTY</c> on handles opened without access rights, so
/// no elevation is needed: of the volume first, which answers for the disk beneath a simple volume, then of each disk
/// the volume lies on. A RAID controller's virtual disk answers neither way (the workstation's C: on a PERC H310, whose
/// media type Windows itself calls unspecified, while its member SSDs answer), so the kind can stay unknown. Thread-free.
/// </summary>
public static class StorageDevice
{
	private const uint FileShareRead = 0x1U;
	private const uint FileShareWrite = 0x2U;
	private const uint OpenExisting = 3U;

	/// <summary>CTL_CODE(IOCTL_STORAGE_BASE, 0x500, METHOD_BUFFERED, FILE_ANY_ACCESS).</summary>
	private const uint IoctlStorageQueryProperty = 0x002D1400U;

	/// <summary>CTL_CODE(IOCTL_VOLUME_BASE, 0, METHOD_BUFFERED, FILE_ANY_ACCESS): the disks a volume lies on.</summary>
	private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000U;

	/// <summary>STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty.</summary>
	private const uint StorageDeviceSeekPenaltyProperty = 7U;

	/// <summary>STORAGE_QUERY_TYPE.PropertyStandardQuery.</summary>
	private const uint PropertyStandardQuery = 0U;

	/// <summary>Disks a volume may span before the question is given up; <c>VOLUME_DISK_EXTENTS</c> is a count, padding and 24 bytes per extent.</summary>
	private const int MaxExtents = 16;

	/// <summary>
	/// Whether the volume holding <paramref name="path"/> incurs a seek penalty: true for a hard disk, or a volume with
	/// one among its disks; false for solid-state drives; null when the question cannot be answered (not Windows, a
	/// relative or UNC path, a volume and disks that do not report the property). The path need not exist; only its
	/// drive letter is used.
	/// </summary>
	public static bool? IncursSeekPenalty(string path)
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}
		string? root = Path.GetPathRoot(path);
		// "C:\" and nothing else: a share or a device path has no volume to ask
		if (root is not { Length: 3 } || root[1] != ':' || root[2] != '\\')
		{
			return null;
		}
		using SafeFileHandle volume = Open(@"\\.\" + root[..2]);
		if (volume.IsInvalid)
		{
			return null;
		}
		if (QuerySeekPenalty(volume) is bool answer)
		{
			return answer;
		}
		// the volume's stack does not forward the question: ask the disks beneath it; a hard disk among them is a seek
		// penalty, every disk answering no is none, a disk without an answer leaves the kind unknown
		uint[] disks = DiskNumbers(volume);
		if (disks.Length == 0)
		{
			return null;
		}
		bool? result = false;
		foreach (uint disk in disks)
		{
			using SafeFileHandle device = Open(@"\\.\PhysicalDrive" + disk.ToString(CultureInfo.InvariantCulture));
			bool? penalty = device.IsInvalid ? null : QuerySeekPenalty(device);
			if (penalty == true)
			{
				return true;
			}
			if (penalty == null)
			{
				result = null;
			}
		}
		return result;
	}

	/// <summary>Opens a volume or disk device for queries alone (no access right); the handle is invalid when it cannot be opened.</summary>
	private static SafeFileHandle Open(string device) => CreateFileW(device, 0U, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0U, IntPtr.Zero);

	/// <summary>The seek-penalty property of a volume or disk; null when its driver does not answer.</summary>
	private static bool? QuerySeekPenalty(SafeFileHandle device)
	{
		var query = new StoragePropertyQuery { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
		if (!DeviceIoControl(device, IoctlStorageQueryProperty, ref query, (uint)Marshal.SizeOf<StoragePropertyQuery>(),
			out DeviceSeekPenaltyDescriptor descriptor, (uint)Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(), out uint returned, IntPtr.Zero)
			|| returned < 9U)
		{
			return null;
		}
		return descriptor.IncursSeekPenalty != 0;
	}

	/// <summary>The numbers of the disks a volume lies on, each once; empty when the volume does not say or spans more than <see cref="MaxExtents"/>.</summary>
	private static uint[] DiskNumbers(SafeFileHandle volume)
	{
		byte[] buffer = new byte[8 + 24 * MaxExtents];
		if (!DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0U, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero) || returned < 8U)
		{
			return [];
		}
		int count = Math.Min((int)BitConverter.ToUInt32(buffer, 0), MaxExtents);
		var disks = new HashSet<uint>();
		for (int i = 0; i < count; i++)
		{
			disks.Add(BitConverter.ToUInt32(buffer, 8 + 24 * i));
		}
		return [.. disks];
	}

	/// <summary>The <c>STORAGE_PROPERTY_QUERY</c> structure: the property, the query type and one byte of parameters.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct StoragePropertyQuery
	{
		public uint PropertyId;
		public uint QueryType;
		public byte AdditionalParameters;
	}

	/// <summary>The <c>DEVICE_SEEK_PENALTY_DESCRIPTOR</c> structure: the header's version and size, then the BOOLEAN.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct DeviceSeekPenaltyDescriptor
	{
		public uint Version;
		public uint Size;
		public byte IncursSeekPenalty;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
	private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, uint inBufferSize,
		out DeviceSeekPenaltyDescriptor outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
		byte[] outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}
