using System;
using System.Collections.Generic;
using System.Text;

namespace V12.Core.Interfaces.Renderer
{
    public struct RendererInfo
    {
        public int FPS;
        public int CurrentObjectsCount;
        public int CurrentTextures;
        public int CurrentAudioSources;
        public int CurrentMaterials;
        public int CurrentLights;
        public long TotalMemoryUsage;

        public RendererInfo(int FPS = 0, int CurrentObjectsCount = 0, int CurrentTextures = 0, int CurrentAudioSources = 0, int CurrentMaterials = 0, int CurrentLights = 0, long TotalMemoryUsage = 0)
        {
            this.FPS = FPS;
            this.CurrentObjectsCount = CurrentObjectsCount;
            this.CurrentTextures = CurrentTextures;
            this.CurrentAudioSources = CurrentAudioSources;
            this.CurrentMaterials = CurrentMaterials;
            this.CurrentLights = CurrentLights;
            this.TotalMemoryUsage = TotalMemoryUsage;
        }
    }
}
