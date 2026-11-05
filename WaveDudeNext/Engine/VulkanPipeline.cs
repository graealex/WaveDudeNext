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

/// <summary>A Vulkan compute pipeline with fixed storage-buffer bindings. One driver thread at a time.</summary>
/// <remarks>The context and bound buffers must outlive the pipeline. Each dispatch waits for completion.</remarks>
internal sealed unsafe class VulkanPipeline : IDisposable
{
	private readonly VulkanComputeContext _context;
	private readonly uint _pushSize;
	private ShaderModule _shader;
	private DescriptorSetLayout _setLayout;
	private PipelineLayout _layout;
	private Pipeline _pipeline;
	private DescriptorPool _pool;
	private DescriptorSet _set;
	private bool _disposed;

	/// <summary>Creates a pipeline from an embedded shader and storage buffers in binding order.</summary>
	/// <param name="context">The owning device.</param>
	/// <param name="shaderName">The shader's file name without extension.</param>
	/// <param name="buffers">Storage buffers in descriptor binding order.</param>
	/// <param name="pushSize">The push-constant size in bytes, a multiple of four.</param>
	/// <param name="fourthOrder">Optional value for specialization constant zero.</param>
	public VulkanPipeline(VulkanComputeContext context, string shaderName, IReadOnlyList<VulkanBuffer> buffers, uint pushSize, bool? fourthOrder = null)
	{
		ArgumentOutOfRangeException.ThrowIfZero(pushSize);
		if (pushSize % 4 != 0 || buffers.Count == 0)
		{
			throw new ArgumentException("A pipeline needs buffers and a four-byte-aligned push-constant size.");
		}
		_context = context;
		_pushSize = pushSize;
		try
		{
			CreateShader(shaderName);
			CreateLayout(buffers.Count);
			CreatePipeline(fourthOrder);
			BindBuffers(buffers);
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	private void CreateShader(string name)
	{
		using Stream stream = typeof(VulkanPipeline).Assembly.GetManifestResourceStream("VulkanShaders/" + name + ".spv")
			?? throw new InvalidDataException("Missing Vulkan shader: " + name);
		var code = new byte[checked((int)stream.Length)];
		stream.ReadExactly(code);
		if (code.Length == 0 || code.Length % 4 != 0)
		{
			throw new InvalidDataException("Invalid Vulkan shader: " + name);
		}
		fixed (byte* pointer = code)
		{
			var create = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)pointer };
			VulkanComputeContext.Check(_context.Api.CreateShaderModule(_context.Device, in create, null, out _shader), "create shader module");
		}
	}

	private void CreateLayout(int count)
	{
		var bindings = new DescriptorSetLayoutBinding[count];
		for (int i = 0; i < count; i++)
		{
			bindings[i] = new DescriptorSetLayoutBinding { Binding = (uint)i, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
		}
		fixed (DescriptorSetLayoutBinding* pointer = bindings)
		{
			var create = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = (uint)count, PBindings = pointer };
			VulkanComputeContext.Check(_context.Api.CreateDescriptorSetLayout(_context.Device, in create, null, out _setLayout), "create descriptor layout");
		}
		var range = new PushConstantRange { StageFlags = ShaderStageFlags.ComputeBit, Size = _pushSize };
		DescriptorSetLayout setLayout = _setLayout;
		var layout = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout, PushConstantRangeCount = 1, PPushConstantRanges = &range };
		VulkanComputeContext.Check(_context.Api.CreatePipelineLayout(_context.Device, in layout, null, out _layout), "create pipeline layout");
	}

	private void CreatePipeline(bool? fourthOrder)
	{
		IntPtr mainName = Marshal.StringToCoTaskMemUTF8("main");
		try
		{
			uint value = fourthOrder == true ? 1u : 0u;
			var entry = new SpecializationMapEntry { ConstantID = 0, Offset = 0, Size = sizeof(uint) };
			var specialization = new SpecializationInfo { MapEntryCount = 1, PMapEntries = &entry, DataSize = sizeof(uint), PData = &value };
			var stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = _shader, PName = (byte*)mainName, PSpecializationInfo = fourthOrder.HasValue ? &specialization : null };
			var create = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo, Stage = stage, Layout = _layout };
			VulkanComputeContext.Check(_context.Api.CreateComputePipelines(_context.Device, default, 1, in create, null, out _pipeline), "create compute pipeline");
		}
		finally
		{
			Marshal.FreeCoTaskMem(mainName);
		}
	}

	private void BindBuffers(IReadOnlyList<VulkanBuffer> buffers)
	{
		var poolSize = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = (uint)buffers.Count };
		var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 1, PPoolSizes = &poolSize };
		VulkanComputeContext.Check(_context.Api.CreateDescriptorPool(_context.Device, in pool, null, out _pool), "create descriptor pool");
		DescriptorSetLayout setLayout = _setLayout;
		var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _pool, DescriptorSetCount = 1, PSetLayouts = &setLayout };
		VulkanComputeContext.Check(_context.Api.AllocateDescriptorSets(_context.Device, in allocate, out _set), "allocate descriptor set");
		var infos = new DescriptorBufferInfo[buffers.Count];
		var writes = new WriteDescriptorSet[buffers.Count];
		fixed (DescriptorBufferInfo* infoPointer = infos)
		fixed (WriteDescriptorSet* writePointer = writes)
		{
			for (int i = 0; i < buffers.Count; i++)
			{
				infos[i] = new DescriptorBufferInfo { Buffer = buffers[i].Handle, Range = (ulong)buffers[i].Length };
				writes[i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = _set, DstBinding = (uint)i, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = infoPointer + i };
			}
			_context.Api.UpdateDescriptorSets(_context.Device, (uint)writes.Length, writePointer, 0, (CopyDescriptorSet*)null);
		}
	}

	/// <summary>Dispatches one grid and waits for it; the caller may then read mapped buffers.</summary>
	public void Dispatch<T>(in T parameters, uint groupsX, uint groupsY = 1) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (sizeof(T) != _pushSize || groupsX == 0 || groupsY == 0)
		{
			throw new ArgumentException("The push constants or grid do not match the pipeline.");
		}
		var allocate = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _context.Pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
		VulkanComputeContext.Check(_context.Api.AllocateCommandBuffers(_context.Device, in allocate, out CommandBuffer command), "allocate command buffer");
		try
		{
			var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
			VulkanComputeContext.Check(_context.Api.BeginCommandBuffer(command, in begin), "begin command buffer");
			_context.Api.CmdBindPipeline(command, PipelineBindPoint.Compute, _pipeline);
			DescriptorSet set = _set;
			_context.Api.CmdBindDescriptorSets(command, PipelineBindPoint.Compute, _layout, 0, 1, &set, 0, (uint*)null);
			T copy = parameters;
			_context.Api.CmdPushConstants(command, _layout, ShaderStageFlags.ComputeBit, 0, _pushSize, &copy);
			_context.Api.CmdDispatch(command, groupsX, groupsY, 1);
			VulkanComputeContext.Check(_context.Api.EndCommandBuffer(command), "end command buffer");
			var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &command };
			VulkanComputeContext.Check(_context.Api.QueueSubmit(_context.Queue, 1, in submit, default), "submit compute work");
			VulkanComputeContext.Check(_context.Api.QueueWaitIdle(_context.Queue), "wait for compute work");
		}
		finally
		{
			_context.Api.FreeCommandBuffers(_context.Device, _context.Pool, 1, in command);
		}
	}

	/// <summary>Releases the pipeline and its descriptors.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (_pool.Handle != 0)
		{
			_context.Api.DestroyDescriptorPool(_context.Device, _pool, null);
		}
		if (_pipeline.Handle != 0)
		{
			_context.Api.DestroyPipeline(_context.Device, _pipeline, null);
		}
		if (_layout.Handle != 0)
		{
			_context.Api.DestroyPipelineLayout(_context.Device, _layout, null);
		}
		if (_setLayout.Handle != 0)
		{
			_context.Api.DestroyDescriptorSetLayout(_context.Device, _setLayout, null);
		}
		if (_shader.Handle != 0)
		{
			_context.Api.DestroyShaderModule(_context.Device, _shader, null);
		}
	}
}
