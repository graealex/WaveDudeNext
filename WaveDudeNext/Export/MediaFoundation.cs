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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WaveDudeNext.Export;

/// <summary>
/// The slice of Media Foundation <see cref="Mp4Writer"/> uses: the flat functions of <c>mfplat.dll</c> and
/// <c>mfreadwrite.dll</c>, hand-written COM interfaces with the vtable order of the SDK headers (a derived
/// COM interface in .NET does not inherit its base's slots, so each one lists every method from
/// <c>IUnknown</c> on, unused ones as placeholders), and the attribute GUIDs. All methods return HRESULTs;
/// <see cref="Check"/> turns a failure into the matching exception. Windows only.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MediaFoundation
{
	/// <summary><c>MF_VERSION</c> of the Windows 7 and later SDK (SDK version 2, API version 0x70).</summary>
	public const int Version = 0x00020070;

	/// <summary><c>eAVEncCommonRateControlMode_Quality</c>.</summary>
	public const int RateControlQuality = 3;

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFStartup(int version, int flags);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFShutdown();

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateMediaType(ref IntPtr mediaType);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateAttributes(ref IntPtr attributes, int initialSize);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateSample(ref IntPtr sample);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateMemoryBuffer(int maxLength, ref IntPtr buffer);

	[DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, ref IntPtr writer);

	/// <summary><c>MFTEnumEx</c>: the registered transforms of a category matching the type filters; the caller releases each activate and frees the array.</summary>
	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFTEnumEx([MarshalAs(UnmanagedType.LPStruct)] Guid category, uint flags, IntPtr inputType, IntPtr outputType, out IntPtr activates, out uint count);

	[DllImport("ole32.dll", ExactSpelling = true)]
	public static extern void CoTaskMemFree(IntPtr ptr);

	/// <summary><c>MFCreateDXGIDeviceManager</c>: the object through which Media Foundation shares a Direct3D 11 device with hardware transforms.</summary>
	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr manager);

	/// <summary><c>D3D11CreateDevice</c> on the default hardware adapter.</summary>
	[DllImport("d3d11.dll", ExactSpelling = true)]
	public static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr immediateContext);

	/// <summary><c>MF_SINK_WRITER_D3D_MANAGER</c>: the device manager the writer hands its transforms.</summary>
	public static readonly Guid SinkWriterD3DManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");

	/// <summary>
	/// A Direct3D 11 device (video support, BGRA, multithread-protected) wrapped in a DXGI device manager for the
	/// sink writer; both released by <see cref="ReleaseDeviceManager"/>. Null when Direct3D refuses.
	/// </summary>
	public static (IntPtr Manager, IntPtr Device)? CreateDeviceManager()
	{
		const int DriverTypeHardware = 1;
		const uint CreateDeviceBgraSupport = 0x20;
		const uint CreateDeviceVideoSupport = 0x800;
		const uint SdkVersion = 7;
		if (D3D11CreateDevice(IntPtr.Zero, DriverTypeHardware, IntPtr.Zero, CreateDeviceBgraSupport | CreateDeviceVideoSupport, IntPtr.Zero, 0, SdkVersion, out IntPtr device, out _, out IntPtr context) < 0)
		{
			return null;
		}
		Marshal.Release(context);
		try
		{
			// Media Foundation's transforms use the device from their own threads
			var multithread = (ID3D10Multithread)Marshal.GetObjectForIUnknown(device);
			try
			{
				multithread.SetMultithreadProtected(1);
			}
			finally
			{
				Marshal.ReleaseComObject(multithread);
			}
			Check(MFCreateDXGIDeviceManager(out uint token, out IntPtr manager));
			var dxgi = (IMFDXGIDeviceManager)Marshal.GetObjectForIUnknown(manager);
			try
			{
				Check(dxgi.ResetDevice(device, token));
			}
			finally
			{
				Marshal.ReleaseComObject(dxgi);
			}
			return (manager, device);
		}
		catch
		{
			Marshal.Release(device);
			throw;
		}
	}

	/// <summary>Releases what <see cref="CreateDeviceManager"/> made.</summary>
	public static void ReleaseDeviceManager((IntPtr Manager, IntPtr Device) pair)
	{
		Marshal.Release(pair.Manager);
		Marshal.Release(pair.Device);
	}

	/// <summary>Sets an IUnknown attribute.</summary>
	public static void SetUnknown(IntPtr attributes, Guid key, IntPtr value)
	{
		var store = (IMFAttributes)Marshal.GetObjectForIUnknown(attributes);
		try
		{
			Check(store.SetUnknown(key, value));
		}
		finally
		{
			Marshal.ReleaseComObject(store);
		}
	}

	/// <summary><c>ID3D10Multithread</c>: the device's lock for use from several threads.</summary>
	[Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface ID3D10Multithread
	{
		[PreserveSig] void Enter();
		[PreserveSig] void Leave();
		[PreserveSig] int SetMultithreadProtected(int protect);
		[PreserveSig] int GetMultithreadProtected();
	}

	/// <summary><c>IMFDXGIDeviceManager</c>; only <c>ResetDevice</c> (slot 5) is called.</summary>
	[Guid("eb533d5d-2db6-40f8-97a9-494692014f07")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFDXGIDeviceManager
	{
		[PreserveSig] int CloseDeviceHandle();
		[PreserveSig] int GetVideoService();
		[PreserveSig] int LockDevice();
		[PreserveSig] int OpenDeviceHandle();
		[PreserveSig] int ResetDevice(IntPtr device, uint resetToken);
		[PreserveSig] int TestDevice();
		[PreserveSig] int UnlockDevice();
	}

	/// <summary><c>MFT_CATEGORY_VIDEO_ENCODER</c>.</summary>
	public static readonly Guid VideoEncoderCategory = new("f79eac7d-e545-4387-bdee-d647d7bde42a");

	/// <summary><c>MFT_FRIENDLY_NAME_Attribute</c>: the transform's display name.</summary>
	public static readonly Guid FriendlyName = new("314ffbae-5b41-4c95-9c19-4e7d586face3");

	/// <summary><c>MFT_ENUM_FLAG_HARDWARE</c>.</summary>
	public const uint EnumHardware = 0x4;

	/// <summary><c>MFT_ENUM_FLAG_SYNCMFT | ASYNCMFT | HARDWARE | SORTANDFILTER</c>: everything, best first.</summary>
	public const uint EnumAll = 0x1 | 0x2 | 0x4 | 0x40;

	/// <summary>The friendly name of a transform or activate (both carry attributes), or null.</summary>
	public static string? FriendlyNameOf(IntPtr unknown)
	{
		object rcw = Marshal.GetObjectForIUnknown(unknown);
		try
		{
			if (rcw is IMFAttributes attributes && attributes.GetAllocatedString(FriendlyName, out IntPtr text, out _) >= 0 && text != IntPtr.Zero)
			{
				string? name = Marshal.PtrToStringUni(text);
				CoTaskMemFree(text);
				return name;
			}
			if (rcw is IMFTransform transform && transform.GetAttributes(out IntPtr attributesPtr) >= 0 && attributesPtr != IntPtr.Zero)
			{
				try
				{
					return FriendlyNameOf(attributesPtr);
				}
				finally
				{
					Marshal.Release(attributesPtr);
				}
			}
			return null;
		}
		finally
		{
			Marshal.ReleaseComObject(rcw);
		}
	}

	/// <summary>The names of the registered H.264 encoder transforms, hardware ones first (<c>MFTEnumEx</c>), for the log.</summary>
	public static List<string> H264Encoders(bool hardwareOnly)
	{
		var names = new List<string>();
		IntPtr outputType = Marshal.AllocHGlobal(32);
		try
		{
			Marshal.Copy(VideoType.ToByteArray(), 0, outputType, 16);
			Marshal.Copy(H264Format.ToByteArray(), 0, outputType + 16, 16);
			if (MFTEnumEx(VideoEncoderCategory, hardwareOnly ? EnumHardware : EnumAll, IntPtr.Zero, outputType, out IntPtr activates, out uint count) < 0 || activates == IntPtr.Zero)
			{
				return names;
			}
			for (int i = 0; i < count; i++)
			{
				IntPtr activate = Marshal.ReadIntPtr(activates, i * IntPtr.Size);
				names.Add(FriendlyNameOf(activate) ?? "(unnamed transform)");
				Marshal.Release(activate);
			}
			CoTaskMemFree(activates);
		}
		finally
		{
			Marshal.FreeHGlobal(outputType);
		}
		return names;
	}

	/// <summary>Throws for a failed HRESULT.</summary>
	public static void Check(int hr)
	{
		if (hr < 0)
		{
			Marshal.ThrowExceptionForHR(hr);
		}
	}

	/// <summary>A new, empty attribute store; the caller releases the pointer.</summary>
	public static IntPtr CreateAttributes()
	{
		IntPtr attributes = IntPtr.Zero;
		Check(MFCreateAttributes(ref attributes, 8));
		return attributes;
	}

	/// <summary>Sets a UINT32 attribute on an attribute store or media type (both implement <c>IMFAttributes</c>).</summary>
	public static void SetUInt32(IntPtr attributes, Guid key, int value)
	{
		var store = (IMFAttributes)Marshal.GetObjectForIUnknown(attributes);
		try
		{
			Check(store.SetUINT32(key, value));
		}
		finally
		{
			Marshal.ReleaseComObject(store);
		}
	}

	/// <summary>Sets a UINT64 attribute.</summary>
	public static void SetUInt64(IntPtr attributes, Guid key, ulong value)
	{
		var store = (IMFAttributes)Marshal.GetObjectForIUnknown(attributes);
		try
		{
			Check(store.SetUINT64(key, value));
		}
		finally
		{
			Marshal.ReleaseComObject(store);
		}
	}

	/// <summary>Sets a GUID attribute.</summary>
	public static void SetGuid(IntPtr attributes, Guid key, Guid value)
	{
		var store = (IMFAttributes)Marshal.GetObjectForIUnknown(attributes);
		try
		{
			Check(store.SetGUID(key, value));
		}
		finally
		{
			Marshal.ReleaseComObject(store);
		}
	}

	// media type attributes
	public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
	public static readonly Guid SubType = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
	public static readonly Guid AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
	public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
	public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
	public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
	public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
	public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
	public static readonly Guid VideoType = new("73646976-0000-0010-8000-00AA00389B71");
	public static readonly Guid H264Format = new("34363248-0000-0010-8000-00AA00389B71");
	public static readonly Guid Rgb32Format = new("00000016-0000-0010-8000-00AA00389B71");
	/// <summary><c>MFVideoFormat_NV12</c> ('NV12'): a Y plane, then interleaved Cb/Cr at half resolution, what the hardware encoders take natively.</summary>
	public static readonly Guid Nv12Format = new("3231564E-0000-0010-8000-00AA00389B71");
	/// <summary><c>MF_MT_VIDEO_NOMINAL_RANGE</c>: 2 = studio range 16..235.</summary>
	public static readonly Guid NominalRange = new("c21b8ee5-b956-4071-8daf-325edf5cab11");

	/// <summary><c>MF_SINK_WRITER_DISABLE_THROTTLING</c>: the writer takes samples as fast as they come instead of pacing the caller.</summary>
	public static readonly Guid DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

	/// <summary><c>MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS</c>: let the sink writer pick a hardware encoder.</summary>
	public static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");

	// encoder properties (ICodecAPI), passed as the sink writer's encoding parameters
	/// <summary><c>CODECAPI_AVEncCommonRateControlMode</c>.</summary>
	public static readonly Guid CodecRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
	/// <summary><c>CODECAPI_AVEncCommonQuality</c>, 0..100.</summary>
	public static readonly Guid CodecQuality = new("fcbf57a3-7ea5-4b0c-9644-69b40c39c391");
	/// <summary><c>CODECAPI_AVEncMPVGOPSize</c>.</summary>
	public static readonly Guid CodecGopSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
	/// <summary><c>CODECAPI_AVEncMPVDefaultBPictureCount</c>.</summary>
	public static readonly Guid CodecBPictureCount = new("8d390aeb-b9cd-47e5-8f82-ac7e3ff6a7a0");
	/// <summary><c>CODECAPI_AVEncCommonQualityVsSpeed</c>, 0 (fastest) to 100 (best).</summary>
	public static readonly Guid CodecQualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");
	/// <summary><c>CODECAPI_AVLowLatencyMode</c> (the same GUID as <c>MF_LOW_LATENCY</c>).</summary>
	public static readonly Guid CodecLowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
	/// <summary><c>CODECAPI_AVEncCommonRealTime</c>: the encoder must keep up with real time rather than take its time.</summary>
	public static readonly Guid CodecRealTime = new("143a0ff6-a131-43da-b81e-98fbb8ec378e");

	/// <summary><c>IMFAttributes</c>; only the three setters are called.</summary>
	[Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFAttributes
	{
		[PreserveSig] int GetItem();
		[PreserveSig] int GetItemType();
		[PreserveSig] int CompareItem();
		[PreserveSig] int Compare();
		[PreserveSig] int GetUINT32();
		[PreserveSig] int GetUINT64();
		[PreserveSig] int GetDouble();
		[PreserveSig] int GetGUID();
		[PreserveSig] int GetStringLength();
		[PreserveSig] int GetString();
		[PreserveSig] int GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out IntPtr value, out int length);
		[PreserveSig] int GetBlobSize();
		[PreserveSig] int GetBlob();
		[PreserveSig] int GetAllocatedBlob();
		[PreserveSig] int GetUnknown();
		[PreserveSig] int SetItem();
		[PreserveSig] int DeleteItem();
		[PreserveSig] int DeleteAllItems();
		[PreserveSig] int SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, int value);
		[PreserveSig] int SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, ulong value);
		[PreserveSig] int SetDouble();
		[PreserveSig] int SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
		[PreserveSig] int SetString();
		[PreserveSig] int SetBlob();
		[PreserveSig] int SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr value);
		[PreserveSig] int LockStore();
		[PreserveSig] int UnlockStore();
		[PreserveSig] int GetCount();
		[PreserveSig] int GetItemByIndex();
		[PreserveSig] int CopyAllItems();
	}

	/// <summary><c>IMFSinkWriter</c>; <c>Finalize</c> is named <see cref="FinalizeWriting"/> to keep clear of <see cref="object.Finalize"/>.</summary>
	[Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFSinkWriter
	{
		[PreserveSig] int AddStream(IntPtr mediaType, ref int streamIndex);
		[PreserveSig] int SetInputMediaType(int streamIndex, IntPtr mediaType, IntPtr encodingParameters);
		[PreserveSig] int BeginWriting();
		[PreserveSig] int WriteSample(int streamIndex, IntPtr sample);
		[PreserveSig] int SendStreamTick();
		[PreserveSig] int PlaceMarker();
		[PreserveSig] int NotifyEndOfSegment();
		[PreserveSig] int Flush();
		[PreserveSig] int FinalizeWriting();
		[PreserveSig] int GetServiceForStream();
		[PreserveSig] int GetStatistics();
	}

	/// <summary><c>IMFSinkWriterEx</c>: <c>IMFSinkWriter</c>'s slots, then <c>GetTransformForStream</c>, which tells which transforms (converter, encoder) serve a stream.</summary>
	[Guid("588d72ab-5bc1-496a-8714-b70617141b25")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFSinkWriterEx
	{
		[PreserveSig] int AddStream(IntPtr mediaType, ref int streamIndex);
		[PreserveSig] int SetInputMediaType(int streamIndex, IntPtr mediaType, IntPtr encodingParameters);
		[PreserveSig] int BeginWriting();
		[PreserveSig] int WriteSample(int streamIndex, IntPtr sample);
		[PreserveSig] int SendStreamTick();
		[PreserveSig] int PlaceMarker();
		[PreserveSig] int NotifyEndOfSegment();
		[PreserveSig] int Flush();
		[PreserveSig] int FinalizeWriting();
		[PreserveSig] int GetServiceForStream();
		[PreserveSig] int GetStatistics();
		[PreserveSig] int GetTransformForStream(int streamIndex, int transformIndex, out Guid category, out IntPtr transform);
	}

	/// <summary><c>IMFTransform</c>; only <c>GetAttributes</c> (slot 6) is called.</summary>
	[Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFTransform
	{
		[PreserveSig] int GetStreamLimits();
		[PreserveSig] int GetStreamCount();
		[PreserveSig] int GetStreamIDs();
		[PreserveSig] int GetInputStreamInfo();
		[PreserveSig] int GetOutputStreamInfo();
		[PreserveSig] int GetAttributes(out IntPtr attributes);
	}

	/// <summary><c>IMFMediaBuffer</c>.</summary>
	[Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFMediaBuffer
	{
		[PreserveSig] int Lock(ref IntPtr data, ref int maxLength, ref int currentLength);
		[PreserveSig] int Unlock();
		[PreserveSig] int GetCurrentLength();
		[PreserveSig] int SetCurrentLength(int length);
		[PreserveSig] int GetMaxLength();
	}

	/// <summary><c>IMFSample</c>: the 30 <c>IMFAttributes</c> slots first (placeholders), then its own methods.</summary>
	[Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport]
	public interface IMFSample
	{
		[PreserveSig] int A01();
		[PreserveSig] int A02();
		[PreserveSig] int A03();
		[PreserveSig] int A04();
		[PreserveSig] int A05();
		[PreserveSig] int A06();
		[PreserveSig] int A07();
		[PreserveSig] int A08();
		[PreserveSig] int A09();
		[PreserveSig] int A10();
		[PreserveSig] int A11();
		[PreserveSig] int A12();
		[PreserveSig] int A13();
		[PreserveSig] int A14();
		[PreserveSig] int A15();
		[PreserveSig] int A16();
		[PreserveSig] int A17();
		[PreserveSig] int A18();
		[PreserveSig] int A19();
		[PreserveSig] int A20();
		[PreserveSig] int A21();
		[PreserveSig] int A22();
		[PreserveSig] int A23();
		[PreserveSig] int A24();
		[PreserveSig] int A25();
		[PreserveSig] int A26();
		[PreserveSig] int A27();
		[PreserveSig] int A28();
		[PreserveSig] int A29();
		[PreserveSig] int A30();
		[PreserveSig] int GetSampleFlags();
		[PreserveSig] int SetSampleFlags();
		[PreserveSig] int GetSampleTime();
		[PreserveSig] int SetSampleTime(long time);
		[PreserveSig] int GetSampleDuration();
		[PreserveSig] int SetSampleDuration(long duration);
		[PreserveSig] int GetBufferCount();
		[PreserveSig] int GetBufferByIndex();
		[PreserveSig] int ConvertToContiguousBuffer();
		[PreserveSig] int AddBuffer(IntPtr buffer);
		[PreserveSig] int RemoveBufferByIndex();
		[PreserveSig] int RemoveAllBuffers();
		[PreserveSig] int GetTotalLength();
		[PreserveSig] int CopyToBuffer();
	}
}
