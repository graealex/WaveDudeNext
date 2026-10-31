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

using WaveDudeNext.Engine;

namespace WaveDudeNext.Runs;

/// <summary>
/// What a first start learns about the machine before choosing the preview quality (2026-10-31, the user's design;
/// <see cref="PreviewDefaults"/> makes the choice): the physical memory, the processor's threads, the OpenCL GPU the
/// engine would take and whether the frame cache's drive incurs a seek penalty. <see cref="Read"/> gathers them in a
/// few milliseconds, most of it the OpenCL enumeration; the record itself is immutable and compared by value, so the
/// harness builds the facts of machines it does not run on. Thread-free.
/// </summary>
/// <param name="PhysicalBytes">Bytes of physical memory as Windows reports them, a little under the nominal size (about 15.9 GB on a 16 GB machine).</param>
/// <param name="Threads">Logical processors.</param>
/// <param name="Gpu">The OpenCL GPU the engine would take, by name, "(shared memory)" added for one integrated with the processor; empty when there is none or OpenCL is missing.</param>
/// <param name="CacheDrive">The frame cache's drive for a log line ("C:"), or the cache folder's root when it has no drive letter.</param>
/// <param name="CacheDriveSeekPenalty">Whether the cache drive incurs a seek penalty, a hard disk; null when Windows cannot say (<see cref="StorageDevice.IncursSeekPenalty"/>).</param>
public sealed record MachineFacts(long PhysicalBytes, int Threads, string Gpu, string CacheDrive, bool? CacheDriveSeekPenalty)
{
	/// <summary>Reads the facts of this machine; a missing OpenCL runtime counts as no GPU.</summary>
	public static MachineFacts Read()
	{
		string gpu = "";
		try
		{
			if (GpuDeviceInfo.FindAll() is [var device, ..])
			{
				gpu = device.Discrete ? device.Name : device.Name + " (shared memory)";
			}
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			// no OpenCL runtime: the CPU engine serves
		}
		string root = Path.GetPathRoot(FrameCache.Directory) ?? FrameCache.Directory;
		return new MachineFacts(FrameMemory.PhysicalBytes(), Environment.ProcessorCount, gpu, root.TrimEnd('\\'), StorageDevice.IncursSeekPenalty(FrameCache.Directory));
	}
}
