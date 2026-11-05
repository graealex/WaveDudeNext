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
using Silk.NET.Vulkan;

namespace WaveDudeNext.Engine;

/// <summary>Owns the Vulkan instance, compute device, queue and command pool. One driver thread at a time.</summary>
internal sealed unsafe class VulkanComputeContext : IDisposable
{
	private const string PortabilityEnumeration = "VK_KHR_portability_enumeration";
	private const string PortabilitySubset = "VK_KHR_portability_subset";
	private readonly Vk _vk;
	private Instance _instance;
	private Device _device;
	private CommandPool _pool;
	private bool _disposed;

	/// <summary>Creates the first suitable GPU and its compute queue; callers dispose the context.</summary>
	public VulkanComputeContext()
	{
		_vk = Vk.GetApi();
		try
		{
			CreateInstance();
			SelectDevice();
			CreateDevice();
			var poolInfo = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = QueueFamily, Flags = CommandPoolCreateFlags.ResetCommandBufferBit };
			Check(_vk.CreateCommandPool(_device, in poolInfo, null, out _pool), "create command pool");
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	/// <summary>The selected Vulkan physical device, valid until disposal.</summary>
	public PhysicalDevice PhysicalDevice { get; private set; }
	/// <summary>The logical device, valid until disposal.</summary>
	public Device Device => _device;
	/// <summary>The compute queue, valid until disposal.</summary>
	public Queue Queue { get; private set; }
	/// <summary>The command pool, valid until disposal.</summary>
	public CommandPool Pool => _pool;
	/// <summary>The compute queue family index.</summary>
	public uint QueueFamily { get; private set; }
	/// <summary>The selected device's display name.</summary>
	public string Name { get; private set; } = "";
	/// <summary>The Vulkan entry points, valid until disposal.</summary>
	public Vk Api => _vk;

	private void CreateInstance()
	{
		uint count = 0;
		Check(_vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, (ExtensionProperties*)null), "enumerate instance extensions");
		var extensions = new ExtensionProperties[count];
		fixed (ExtensionProperties* pointer = extensions)
		{
			Check(_vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, pointer), "enumerate instance extensions");
		}
		bool portability = extensions.Any(extension => ExtensionName(extension) == PortabilityEnumeration);
		IntPtr name = portability ? Marshal.StringToCoTaskMemUTF8(PortabilityEnumeration) : IntPtr.Zero;
		try
		{
			byte* namePointer = (byte*)name;
			var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
			var create = new InstanceCreateInfo
			{
				SType = StructureType.InstanceCreateInfo,
				PApplicationInfo = &app,
				Flags = portability ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0,
				EnabledExtensionCount = portability ? 1u : 0u,
				PpEnabledExtensionNames = portability ? &namePointer : null,
			};
			Check(_vk.CreateInstance(in create, null, out _instance), "create instance");
		}
		finally
		{
			Marshal.FreeCoTaskMem(name);
		}
	}

	private void SelectDevice()
	{
		uint count = 0;
		Check(_vk.EnumeratePhysicalDevices(_instance, ref count, (PhysicalDevice*)null), "enumerate physical devices");
		var devices = new PhysicalDevice[count];
		fixed (PhysicalDevice* pointer = devices)
		{
			Check(_vk.EnumeratePhysicalDevices(_instance, ref count, pointer), "enumerate physical devices");
		}
		int best = -1;
		foreach (PhysicalDevice device in devices)
		{
			_vk.GetPhysicalDeviceProperties(device, out PhysicalDeviceProperties properties);
			if (properties.ApiVersion < Vk.Version13 || properties.DeviceType == PhysicalDeviceType.Cpu)
			{
			continue;
			}
			uint queueCount = 0;
			_vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueCount, (QueueFamilyProperties*)null);
			var queues = new QueueFamilyProperties[queueCount];
			fixed (QueueFamilyProperties* pointer = queues)
			{
				_vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueCount, pointer);
			}
			for (uint i = 0; i < queueCount; i++)
			{
				if (queues[i].QueueCount == 0 || (queues[i].QueueFlags & QueueFlags.ComputeBit) == 0)
				{
					continue;
				}
				int rank = properties.DeviceType == PhysicalDeviceType.DiscreteGpu ? 1 : 0;
				if (rank > best)
				{
					best = rank;
					PhysicalDevice = device;
					QueueFamily = i;
					Name = Marshal.PtrToStringUTF8((IntPtr)properties.DeviceName) ?? "GPU";
				}
				break;
			}
		}
		if (best < 0)
		{
			throw new InvalidOperationException("No Vulkan 1.3 GPU with a compute queue was found.");
		}
	}

	private void CreateDevice()
	{
		uint count = 0;
		Check(_vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, ref count, (ExtensionProperties*)null), "enumerate device extensions");
		var extensions = new ExtensionProperties[count];
		fixed (ExtensionProperties* pointer = extensions)
		{
			Check(_vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, ref count, pointer), "enumerate device extensions");
		}
		bool portability = extensions.Any(extension => ExtensionName(extension) == PortabilitySubset);
		IntPtr name = portability ? Marshal.StringToCoTaskMemUTF8(PortabilitySubset) : IntPtr.Zero;
		try
		{
			byte* namePointer = (byte*)name;
			float priority = 1f;
			var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = QueueFamily, QueueCount = 1, PQueuePriorities = &priority };
			var create = new DeviceCreateInfo
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = 1,
				PQueueCreateInfos = &queueInfo,
				EnabledExtensionCount = portability ? 1u : 0u,
				PpEnabledExtensionNames = portability ? &namePointer : null,
			};
			Check(_vk.CreateDevice(PhysicalDevice, in create, null, out _device), "create device");
			_vk.GetDeviceQueue(_device, QueueFamily, 0, out Queue queue);
			Queue = queue;
		}
		finally
		{
			Marshal.FreeCoTaskMem(name);
		}
	}

	private static string? ExtensionName(ExtensionProperties extension) => Marshal.PtrToStringUTF8((IntPtr)extension.ExtensionName);

	/// <summary>Throws a diagnostic when a Vulkan call fails.</summary>
	public static void Check(Result result, string operation)
	{
		if (result != Result.Success)
		{
			throw new InvalidOperationException("Vulkan " + operation + " failed: " + result);
		}
	}

	/// <summary>Releases the command pool, device and instance after pending work completes.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (_device.Handle != 0)
		{
			_vk.DeviceWaitIdle(_device);
			if (_pool.Handle != 0)
			{
				_vk.DestroyCommandPool(_device, _pool, null);
			}
			_vk.DestroyDevice(_device, null);
		}
		if (_instance.Handle != 0)
		{
			_vk.DestroyInstance(_instance, null);
		}
		_vk.Dispose();
	}
}
