using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using WarcraftPlugin.Diagnostics;
using EngineTrace = CounterStrikeSharp.API.Modules.Utils.Trace;

namespace WarcraftPlugin.Helpers
{
    public static class RayTracer
    {
        internal enum TraceState { Failed, Clear, Hit }

        internal readonly record struct LineTrace(TraceState State, Vector? End, nint HitEntity)
        {
            internal bool Reaches(nint target) =>
                State == TraceState.Clear || (State == TraceState.Hit && target != nint.Zero && HitEntity == target);
        }

        private static readonly TraceOptions Options = new() { InteractsWith = (Contents)0x1C1003UL };

        public static Vector? Trace(Vector _origin, QAngle _viewangles, bool drawResult = false, bool fromPlayer = false)
        {
            if (fromPlayer)
            {
                var forward = new Vector();
                NativeAPI.AngleVectors(_viewangles.Handle, forward.Handle, nint.Zero, nint.Zero);
                var start = _origin.Clone().Add(x: forward.X * 50, y: forward.Y * 50, z: forward.Z * 50);
                var end = _origin.Clone().Add(x: forward.X * 8192, y: forward.Y * 8192, z: forward.Z * 8192);
                return Trace(start, end, drawResult);
            }

            return HitPosition(TraceView(_origin, _viewangles, null, drawResult));
        }

        public static Vector? Trace(Vector _origin, Vector _endOrigin, bool drawResult = false) =>
            HitPosition(TraceLine(_origin, _endOrigin, drawResult: drawResult));

        internal static LineTrace TraceView(Vector origin, QAngle angles, CBaseEntity? ignoreEntity, bool drawResult)
        {
            LineTrace result;
            try
            {
                result = ReadResult(EngineTrace.TraceShape(origin, angles, ignoreEntity, Options));
            }
            catch (Exception ex)
            {
                PersistentLogger.Error(nameof(RayTracer), "CounterStrikeSharp view trace failed.", ex, mirrorConsole: false);
                return default;
            }

            DrawResult(origin, result, drawResult);
            return result;
        }

        internal static LineTrace TraceLine(Vector origin, Vector end, CBaseEntity? ignoreEntity = null, bool drawResult = false)
        {
            LineTrace result;
            try
            {
                result = ReadResult(EngineTrace.TraceEndShape(origin, end, ignoreEntity, Options));
            }
            catch (Exception ex)
            {
                PersistentLogger.Error(nameof(RayTracer), "CounterStrikeSharp line trace failed.", ex, mirrorConsole: false);
                return default;
            }

            DrawResult(origin, result, drawResult);
            return result;
        }

        internal static Vector? HitPosition(LineTrace result) => result.State == TraceState.Hit ? result.End : null;

        private static LineTrace ReadResult(TraceResult result)
        {
            if (!float.IsFinite(result.Fraction) || result.Fraction < 0 || result.Fraction > 1)
                return default;

            var endpoint = result.EndPos;
            if (!float.IsFinite(endpoint.X) || !float.IsFinite(endpoint.Y) || !float.IsFinite(endpoint.Z))
                return default;

            return result.DidHit()
                ? new(TraceState.Hit, endpoint, result.HitEntity().Handle)
                : new(TraceState.Clear, endpoint, nint.Zero);
        }

        private static void DrawResult(Vector origin, LineTrace result, bool drawResult)
        {
            if (drawResult && result.End != null)
                Warcraft.DrawLaserBetween(origin, result.End, result.State == TraceState.Hit ? Color.Red : Color.Green, 5);
        }
    }
}
