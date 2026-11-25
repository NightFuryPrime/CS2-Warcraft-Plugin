using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using System.Runtime.InteropServices;

namespace WarcraftPlugin.Helpers
{
    internal static class Memory
    {
        public static MemoryFunctionWithReturn<nint, nint, nint, uint, nint, uint, uint, byte> CSoundOpGameSystem_SetSoundEventParam_Windows =
            new("48 89 5C 24 08 48 89 6C 24 10 56 57 41 56 48 83 EC 40 48 8B B4 24 80 00 00 00");
        public static MemoryFunctionWithReturn<int, int, nint, uint, nint, short, uint, nint> CSoundOpGameSystem_SetSoundEventParam_Linux =
            new("55 48 89 E5 41 57 49 89 F7 41 56 45 89 CE 41 55 49 89 CD");

    }

    internal class Struct
    {
        [StructLayout(LayoutKind.Explicit)]
        internal struct CAttackerInfo
        {
            internal CAttackerInfo(CEntityInstance attacker)
            {
                NeedInit = false;
                IsWorld = true;
                Attacker = attacker.EntityHandle.Raw;
                if (attacker.DesignerName != "cs_player_controller") return;

                var controller = attacker.As<CCSPlayerController>();
                IsWorld = false;
                IsPawn = true;
                AttackerUserId = (ushort)(controller.UserId ?? 0xFFFF);
                TeamNum = controller.TeamNum;
                TeamChecked = controller.TeamNum;
            }

            [FieldOffset(0x0), MarshalAs(UnmanagedType.U1)] internal bool NeedInit = true;
            [FieldOffset(0x1), MarshalAs(UnmanagedType.U1)] internal bool IsPawn = false;
            [FieldOffset(0x2), MarshalAs(UnmanagedType.U1)] internal bool IsWorld = false;

            [FieldOffset(0x4)]
            internal uint Attacker;

            [FieldOffset(0x8)]
            internal ushort AttackerUserId;

            [FieldOffset(0x0C)] internal int TeamChecked = -1;
            [FieldOffset(0x10)] internal int TeamNum = -1;
        }
    }
}
