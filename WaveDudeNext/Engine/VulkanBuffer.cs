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

using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace WaveDudeNext.Engine;

/// <summary>A host-visible coherent Vulkan storage buffer, owned and used on one driver thread.</summary>
/// <remarks>The context must outlive the buffer. The caller waits for GPU work before reading or disposing it.</remarks>
internal sealed unsafe class VulkanBuffer : IDisposable
{
	private readonly VulkanComputeContext _context;
	private readonly int _length;
	private Buffer _buffer;
	private DeviceMemory _memory;
	private void* _mapped;
	private bool _disposed;

	/// <summary>Allocates and maps a storage buffer of <paramref name="length"/> bytes.</summary>
	public VulkanBuffer(VulkanComputeContext context, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		_context = context;
		_length = length;
		try
		{
			var create = new BufferCreateInfo
			{
				SType = StructureType.BufferCreateInfo,
				Size = (ulong)length,
				Usage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
				SharingMode = SharingMode.Exclusive,
			};
			VulkanComputeContext.Check(context.Api.CreateBuffer(context.Device, in create, null, out _buffer), "create buffer");
			context.Api.GetBufferMemoryRequirements(context.Device, _buffer, out MemoryRequirements requirements);
			context.Api.GetPhysicalDeviceMemoryProperties(context.PhysicalDevice, out PhysicalDeviceMemoryProperties properties);
			MemoryPropertyFlags needed = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
			uint index = 0;
			for (; index < properties.MemoryTypeCount; index++)
			{
				if ((requirements.MemoryTypeBits & (1u << (int)index)) != 0 && (properties.MemoryTypes[(int)index].PropertyFlags & needed) == needed)
				{
					break;
				}
			}
			if (index == properties.MemoryTypeCount)
			{
				throw new InvalidOperationException("No host-visible coherent Vulkan memory is available for the buffer.");
			}
			var allocate = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = index };
			VulkanComputeContext.Check(context.Api.AllocateMemory(context.Device, in allocate, null, out _memory), "allocate buffer memory");
			VulkanComputeContext.Check(context.Api.BindBufferMemory(context.Device, _buffer, _memory, 0), "bind buffer memory");
			void* address;
			VulkanComputeContext.Check(context.Api.MapMemory(context.Device, _memory, 0, (ulong)length, 0, &address), "map buffer memory");
			_mapped = address;
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	/// <summary>The Vulkan buffer handle, valid until disposal.</summary>
	public Buffer Handle => !_disposed ? _buffer : throw new ObjectDisposedException(nameof(VulkanBuffer));
	/// <summary>Bytes addressable by the buffer.</summary>
	public int Length => _length;
	/// <summary>The mapped bytes; use only after preceding GPU work has completed.</summary>
	public Span<byte> Bytes => !_disposed ? new Span<byte>(_mapped, _length) : throw new ObjectDisposedException(nameof(VulkanBuffer));

	/// <summary>Unmaps and frees the buffer. The caller has waited for any GPU use to finish.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (_mapped != null)
		{
			_context.Api.UnmapMemory(_context.Device, _memory);
		}
		if (_buffer.Handle != 0)
		{
			_context.Api.DestroyBuffer(_context.Device, _buffer, null);
		}
		if (_memory.Handle != 0)
		{
			_context.Api.FreeMemory(_context.Device, _memory, null);
		}
	}
}
