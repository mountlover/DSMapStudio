﻿using StudioCore.DebugPrimitives;
//using MeowDSIO.DataTypes.FLVER;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Veldrid;
using Veldrid.Utilities;
using StudioCore.Scene;

namespace StudioCore.Scene
{
    public class CollisionRenderer : RenderObject, IDisposable
    {
        public BoundingBox Bounds;

        private bool HasNoLODs = true;

        private Resource.ResourceHandle<Resource.HavokCollisionResource> ColResource;
        private int ColMeshIndex;

        bool WorldDirty = false;
        private Matrix4x4 _World = Matrix4x4.Identity;
        public Matrix4x4 WorldTransform
        {
            get
            {
                return _World;
            }
            set
            {
                _World = value;
                WorldDirty = true;
            }
        }

        protected Pipeline RenderPipeline;
        protected Shader[] Shaders;
        protected DeviceBuffer WorldBuffer;
        protected ResourceSet PerObjRS;

        public int VertexCount { get; private set; }

        public readonly CollisionMesh Parent;

        public bool IsVisible { get; set; } = true;

        public CollisionRenderer(CollisionMesh parent, Resource.ResourceHandle<Resource.HavokCollisionResource> resourceHandle, int meshIndex)
        {
            ColResource = resourceHandle;
            ColMeshIndex = meshIndex;
        }

        public override void CreateDeviceObjects(GraphicsDevice gd, CommandList cl, SceneRenderPipeline sp)
        {
            var factory = gd.ResourceFactory;
            WorldBuffer = factory.CreateBuffer(new BufferDescription(64, Veldrid.BufferUsage.UniformBuffer | Veldrid.BufferUsage.Dynamic));
            gd.UpdateBuffer(WorldBuffer, 0, ref _World, 64);

            ResourceLayout projViewCombinedLayout = StaticResourceCache.GetResourceLayout(
                gd.ResourceFactory,
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ViewProjection", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceLayout worldLayout = StaticResourceCache.GetResourceLayout(gd.ResourceFactory, new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("World", ResourceKind.UniformBuffer, ShaderStages.Vertex, ResourceLayoutElementOptions.DynamicBinding)));

            VertexLayoutDescription[] mainVertexLayouts = new VertexLayoutDescription[]
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("position", VertexElementSemantic.TextureCoordinate, Veldrid.VertexElementFormat.Float3),
                    new VertexElementDescription("normal", VertexElementSemantic.TextureCoordinate, Veldrid.VertexElementFormat.SByte4),
                    new VertexElementDescription("color", VertexElementSemantic.TextureCoordinate, Veldrid.VertexElementFormat.Byte4))
            };

            var res = StaticResourceCache.GetShaders(gd, gd.ResourceFactory, "Collision").ToTuple();
            Shaders = new Shader[] { res.Item1, res.Item2 };

            ResourceLayout projViewLayout = StaticResourceCache.GetResourceLayout(
                gd.ResourceFactory,
                StaticResourceCache.ProjViewLayoutDescription);

            ResourceLayout mainPerObjectLayout = StaticResourceCache.GetResourceLayout(gd.ResourceFactory, new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment, ResourceLayoutElementOptions.DynamicBinding)));


            PerObjRS = factory.CreateResourceSet(new ResourceSetDescription(mainPerObjectLayout,
                WorldBuffer));

            bool isTriStrip = false;
            var fres = ColResource.Get();
            if (fres != null)
            {
                var mesh = fres.GPUMeshes[ColMeshIndex];
            }

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
            pipelineDescription.DepthStencilState = gd.IsDepthRangeZeroToOne ? DepthStencilStateDescription.DepthOnlyLessEqual : DepthStencilStateDescription.DepthOnlyGreaterEqual;
            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.CounterClockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);
            pipelineDescription.PrimitiveTopology = isTriStrip ? PrimitiveTopology.TriangleStrip : PrimitiveTopology.TriangleList;
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: mainVertexLayouts,
                shaders: Shaders);
            pipelineDescription.ResourceLayouts = new ResourceLayout[] { projViewLayout, mainPerObjectLayout };
            pipelineDescription.Outputs = gd.SwapchainFramebuffer.OutputDescription;
            RenderPipeline = factory.CreateGraphicsPipeline(pipelineDescription);
        }

        public override void DestroyDeviceObjects()
        {
            throw new NotImplementedException();
        }

        public override void UpdatePerFrameResources(GraphicsDevice gd, CommandList cl, SceneRenderPipeline sp)
        {
            if (WorldDirty)
            {
                cl.UpdateBuffer(WorldBuffer, 0, ref _World, 64);
                WorldDirty = false;
            }
        }

        public override void Render(GraphicsDevice gd, CommandList cl, SceneRenderPipeline sp)
        {
            if (!IsVisible)
                return;

            if (ColResource == null || !ColResource.IsLoaded || ColResource.Get() == null)
                return;

            if (ColResource.TryLock())
            {
                var resource = ColResource.Get();
                var mesh = resource.GPUMeshes[ColMeshIndex];
                var vertbuffer = mesh.VertBuffer;

                cl.SetPipeline(RenderPipeline);
                cl.SetGraphicsResourceSet(0, sp.ProjViewRS);
                uint offset = 0;
                cl.SetGraphicsResourceSet(1, PerObjRS, 1, ref offset);
                cl.SetVertexBuffer(0, vertbuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed(mesh.IndexBuffer.SizeInBytes / 4u, 1, 0, 0, 0);
                ColResource.Unlock();
            }
        }

        public void Dispose()
        {

            //VertBuffer.Dispose();

            // Just leave the texture data as-is, since 
            // TexturePool handles memory cleanup
        }
    }
}
