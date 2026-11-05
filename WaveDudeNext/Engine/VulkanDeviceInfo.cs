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

// Contributors: Vinicius Miguel

using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace WaveDudeNext.Engine;

/// <summary>A Vulkan device's capabilities relevant to a wave engine. Thread-free.</summary>
/// <param name="Name">The driver-reported device name.</param>
/// <param name="ApiVersion">The packed Vulkan API version.</param>
/// <param name="Discrete">Whether the device is a discrete GPU.</param>
/// <param name="Software">Whether the device is a CPU implementation.</param>
/// <param name="ComputeQueueFamily">Index of a queue family that supports compute.</param>
public sealed record VulkanDeviceInfo(string Name, uint ApiVersion, bool Discrete, bool Software, uint ComputeQueueFamily)
{
	/// <summary>The Vulkan device UUID as 16 bytes in driver order.</summary>
	public byte[] DeviceUuid { get; private init; } = [];
	/// <summary>Whether opaque native semaphore handles can be both exported and imported on Windows or Linux. False on other platforms.</summary>
	public bool OpaqueSemaphoreSharing { get; private init; }

	private static unsafe bool SupportsOpaqueSemaphoreSharing(Vk vk, PhysicalDevice device)
	{
		string extension;
		ExternalSemaphoreHandleTypeFlags handle;
		if (OperatingSystem.IsWindows()) { extension = "VK_KHR_external_semaphore_win32"; handle = ExternalSemaphoreHandleTypeFlags.OpaqueWin32Bit; }
		else if (OperatingSystem.IsLinux()) { extension = "VK_KHR_external_semaphore_fd"; handle = ExternalSemaphoreHandleTypeFlags.OpaqueFDBit; }
		else return false;
		uint count = 0;
		Check(vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, (ExtensionProperties*)null));
		var extensions = new ExtensionProperties[count];
		fixed (ExtensionProperties* pointer = extensions) Check(vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, pointer));
		if (!extensions.Any(item => Marshal.PtrToStringUTF8((IntPtr)item.ExtensionName) == extension)) return false;
		var query = new PhysicalDeviceExternalSemaphoreInfo { SType = StructureType.PhysicalDeviceExternalSemaphoreInfo, HandleType = handle };
		var properties = new ExternalSemaphoreProperties { SType = StructureType.ExternalSemaphoreProperties };
		vk.GetPhysicalDeviceExternalSemaphoreProperties(device, &query, &properties);
		const ExternalSemaphoreFeatureFlags required = ExternalSemaphoreFeatureFlags.ExportableBit | ExternalSemaphoreFeatureFlags.ImportableBit;
		return (properties.ExternalSemaphoreFeatures & required) == required;
	}

	internal static unsafe byte[] ReadDeviceUuid(Vk vk, PhysicalDevice device)
	{
		var id = new PhysicalDeviceIDProperties { SType = StructureType.PhysicalDeviceIDProperties };
		var properties = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &id };
		vk.GetPhysicalDeviceProperties2(device, &properties);
		return new ReadOnlySpan<byte>(id.DeviceUuid, 16).ToArray();
	}
	/// <summary>
	/// Enumerates Vulkan 1.3 devices with a compute queue, discrete GPUs first. The portability-enumeration
	/// extension is enabled when present so MoltenVK devices are included. Handles do not outlive this call.
	/// </summary>
	/// <exception cref="InvalidOperationException">The loader or driver failed a Vulkan enumeration call.</exception>
	public static unsafe IReadOnlyList<VulkanDeviceInfo> FindAll()
	{
		using var vk = Vk.GetApi();
		uint extensionCount = 0;
		Check(vk.EnumerateInstanceExtensionProperties((byte*)null, ref extensionCount, (ExtensionProperties*)null));
		var extensions = new ExtensionProperties[extensionCount];
		fixed (ExtensionProperties* properties = extensions)
		{
			Check(vk.EnumerateInstanceExtensionProperties((byte*)null, ref extensionCount, properties));
		}
		bool portability = false;
		foreach (ExtensionProperties extension in extensions)
		{
			portability |= Marshal.PtrToStringUTF8((IntPtr)extension.ExtensionName) == "VK_KHR_portability_enumeration";
		}
		IntPtr portabilityName = portability ? Marshal.StringToCoTaskMemUTF8("VK_KHR_portability_enumeration") : IntPtr.Zero;
		try
		{
			byte* extensionName = (byte*)portabilityName;
			ApplicationInfo app = new() { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
			InstanceCreateInfo create = new()
			{
				SType = StructureType.InstanceCreateInfo,
				PApplicationInfo = &app,
				Flags = portability ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0,
				EnabledExtensionCount = portability ? 1u : 0u,
				PpEnabledExtensionNames = portability ? &extensionName : null,
			};
			Check(vk.CreateInstance(in create, null, out Instance instance));
			try
			{
				uint count = 0;
				Check(vk.EnumeratePhysicalDevices(instance, ref count, (PhysicalDevice*)null));
				var devices = new PhysicalDevice[count];
				fixed (PhysicalDevice* devicePointer = devices)
				{
					Check(vk.EnumeratePhysicalDevices(instance, ref count, devicePointer));
				}
				var found = new List<VulkanDeviceInfo>();
				foreach (PhysicalDevice device in devices)
				{
					vk.GetPhysicalDeviceProperties(device, out PhysicalDeviceProperties properties);
					if (properties.ApiVersion < Vk.Version13)
					{
						continue;
					}
					uint queueCount = 0;
					vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueCount, (QueueFamilyProperties*)null);
					var queues = new QueueFamilyProperties[queueCount];
					fixed (QueueFamilyProperties* queuePointer = queues)
					{
						vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueCount, queuePointer);
					}
					for (uint i = 0; i < queueCount; i++)
					{
						if (queues[i].QueueCount > 0 && (queues[i].QueueFlags & QueueFlags.ComputeBit) != 0)
						{
							string name = Marshal.PtrToStringUTF8((IntPtr)properties.DeviceName) ?? "GPU";
							found.Add(new VulkanDeviceInfo(name, properties.ApiVersion, properties.DeviceType == PhysicalDeviceType.DiscreteGpu, properties.DeviceType == PhysicalDeviceType.Cpu, i)
							{
								DeviceUuid = ReadDeviceUuid(vk, device),
								OpaqueSemaphoreSharing = SupportsOpaqueSemaphoreSharing(vk, device),
							});
							break;
						}
					}
				}
				return found.OrderBy(device => device.Software).ThenByDescending(device => device.Discrete).ToArray();
			}
			finally
			{
				vk.DestroyInstance(instance, null);
			}
		}
		finally
		{
			Marshal.FreeCoTaskMem(portabilityName);
		}
	}

	private static void Check(Result result)
	{
		if (result != Result.Success)
		{
			throw new InvalidOperationException("Vulkan device enumeration failed: " + result);
		}
	}
}
