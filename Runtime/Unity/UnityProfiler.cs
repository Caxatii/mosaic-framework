using System.Collections.Generic;
using Mosaic.Core;
using Unity.Profiling;

namespace Mosaic.Infrastructure
{
    /// <summary>
    /// Shows simulation work in the Unity Profiler as markers ("Tick WeaponSystem", "AttackCommand",
    /// "DamageApplied → HitFeedbackPresenter"), also in development builds on devices. No allocations once every
    /// marker exists. <c>context.Profiler = new UnityProfiler();</c>
    /// </summary>
    public sealed class UnityProfiler : ISimulationProfiler
    {
        private readonly Dictionary<string, ProfilerMarker> _markers = new Dictionary<string, ProfilerMarker>();
        private readonly Stack<ProfilerMarker> _open = new Stack<ProfilerMarker>();

        public void Begin(string name)
        {
            if (!_markers.TryGetValue(name, out var marker))
                _markers.Add(name, marker = new ProfilerMarker(ProfilerCategory.Scripts, "Mosaic " + name));

            marker.Begin();
            _open.Push(marker);
        }

        public void End()
        {
            if (_open.Count > 0)
                _open.Pop().End();
        }
    }
}
