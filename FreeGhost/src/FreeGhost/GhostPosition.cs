using HarmonyLib;
using System;
using UnityEngine;

namespace FreeGhost
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // GhostPositionSync - shows the free-flying ghost to EVERY player at desiredPosition,
    // including players who do not have any mod installed.
    //
    // Every client places a remote ghost at
    //     target.Center - spectateZoom * lookDirection + 0.5 * up        (PlayerGhost.Update)
    // and lookDirection comes from the synced lookValues (yaw/pitch). Both values travel as half
    // floats in the normal character packet. This rewrites ONLY the outgoing copy of that packet
    // (CharacterSyncer.GetDataToWrite) for the local, dead player, so the formula lands on
    // desiredPosition. The local game state is not touched, and no new network messages are added.
    //
    // Encoding idea after lllei's FreeGhost 1.2.0 (MIT License, Copyright (c) 2026 lllei,
    // https://github.com/llleixx/FreeGhost).
    //
    // Usage: GhostPositionSync.SetPosition(desiredPosition);  every frame while the free cam runs.
    //        The Harmony patch at the bottom is picked up by harmony.PatchAll().
    // 
    public class GhostPosition
    {
        public static class GhostPositionSync
        {
            /// <summary>Farthest synced distance from the spectated player in meters. Farther away the
            /// ghost stops at this distance, still in the right direction.</summary>
            public static float MaxDistance = 1000f;

            // Receivers clamp pitch to +-85 degrees (CharacterMovement.CameraLook runs for remote players too).
            private const float PitchLimit = 85f;
            // Yaw has to stay continuous (see Rewrite), so it grows when you fly around the target.
            // Above 1024 a half float only has 1-degree steps, so it is wrapped back before that.
            private const float YawHardLimit = 1000f;
            // Close to the target a wrap is barely visible, so there it already happens after 1.5 turns.
            private const float YawCheapLimit = 540f;
            private const float CheapWrapDistance = 3f;
            private const float GhostUpOffset = 0.5f;

            private static readonly BepInEx.Logging.ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("FreeGhost.Sync");

            private static bool _active;
            private static Vector3 _position;
            private static PlayerGhost _ghost;     // ghost the position belongs to; a new death means a new ghost
            private static Character _lastTarget;
            private static bool _hasLastYaw;
            private static float _lastYaw;
            private static bool _errorLogged;
            private static bool _missingLogged;

            /// <summary>True while positions are being sent instead of the vanilla orbit.</summary>
            public static bool IsActive => _active;

            /// <summary>Call every frame while the free cam is active. The last position keeps being sent
            /// (for example while a menu is open) until Stop() is called, the player is revived,
            /// or a new ghost appears.</summary>
            public static void SetPosition(Vector3 worldPosition)
            {
                if (!IsFinite(worldPosition))
                    return;
                Character local = Character.localCharacter;
                PlayerGhost ghost = local != null ? local.Ghost : null;
                if (ghost == null)
                {
                    Stop();
                    return;
                }
                if (ghost != _ghost)
                {
                    _ghost = ghost;
                    _hasLastYaw = false;
                }
                _position = worldPosition;
                _active = true;
            }

            /// <summary>Back to the vanilla ghost (orbiting the spectated player). Only needed if the
            /// free cam is turned off while still dead.</summary>
            public static void Stop()
            {
                _active = false;
                _ghost = null;
                _lastTarget = null;
                _hasLastYaw = false;
            }

            internal static void LogMissingTarget()
            {
                if (_missingLogged)
                    return;
                _missingLogged = true;
                Log.LogError("CharacterSyncer.GetDataToWrite not found - ghost position sync is off, the rest of the mod still works.");
            }

            // Called for every outgoing character packet. Never throws; on any problem the packet stays vanilla.
            internal static void Rewrite(CharacterSyncer syncer, ref CharacterSyncData data)
            {
                if (!_active)
                    return;
                try
                {
                    Character local = Character.localCharacter;
                    if (local == null || local.data == null || !local.data.dead || local.Ghost == null || local.Ghost != _ghost)
                    {
                        Stop();
                        return;
                    }
                    if (syncer == null || syncer.gameObject != local.gameObject)
                        return;   // somebody else's packet (e.g. a bot the host owns)

                    Character target = _ghost.m_target;
                    if (target == null)
                        return;

                    Vector3 offset = _position - (target.Center + Vector3.up * GhostUpOffset);
                    float dist = offset.magnitude;
                    if (!IsFinite(dist))
                        return;
                    if (dist > MaxDistance)
                    {
                        offset *= MaxDistance / dist;
                        dist = MaxDistance;
                    }

                    // Receivers put the ghost at origin - zoom * dir, so dir points from the ghost to the target.
                    float yaw;
                    float pitch = 0f;
                    if (dist < 0.001f)
                    {
                        yaw = _hasLastYaw ? _lastYaw : data.lookValues.x;   // zoom 0: the direction does not matter
                    }
                    else
                    {
                        Vector3 dir = -offset / dist;
                        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                        pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                    }
                    pitch = Mathf.Clamp(pitch, -PitchLimit, PitchLimit);

                    // Receivers lerp lookValues between packets. A jump of 360 degrees would swing the ghost
                    // once around the target, so yaw stays continuous with what was sent before.
                    // A new target makes the ghost jump anyway, so that is a free moment to start fresh.
                    bool fresh = !_hasLastYaw || target != _lastTarget;
                    if (!fresh)
                        yaw += 360f * Mathf.Round((_lastYaw - yaw) / 360f);
                    float absYaw = Mathf.Abs(yaw);
                    if (fresh || absYaw > YawHardLimit || (absYaw > YawCheapLimit && dist < CheapWrapDistance) || !IsFinite(yaw))
                        yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;

                    // Round exactly like the packet does (half floats), then pick the best zoom for that direction.
                    float yawQ = (Unity.Mathematics.half)yaw;
                    float pitchQ = (Unity.Mathematics.half)pitch;
                    Vector3 dirQ = LookToDirection(yawQ, pitchQ);
                    float zoomQ = (Unity.Mathematics.half)Mathf.Max(0f, -Vector3.Dot(offset, dirQ));
                    if (!IsFinite(yawQ) || !IsFinite(pitchQ) || !IsFinite(zoomQ))
                        return;

                    data.lookValues = new Unity.Mathematics.float2(yawQ, pitchQ);
                    data.spectateZoom = zoomQ;
                    _lastYaw = yawQ;
                    _hasLastYaw = true;
                    _lastTarget = target;
                }
                catch (Exception e)
                {
                    if (!_errorLogged)
                    {
                        _errorLogged = true;
                        Log.LogError("Ghost position sync failed, packet left vanilla: " + e.Message);
                    }
                }
            }

            // Same as the game's HelperFunctions.LookToDirection: Quaternion.Euler(-pitch, yaw, 0) * forward.
            private static Vector3 LookToDirection(float yaw, float pitch)
            {
                float y = yaw * Mathf.Deg2Rad;
                float p = pitch * Mathf.Deg2Rad;
                float c = Mathf.Cos(p);
                return new Vector3(Mathf.Sin(y) * c, Mathf.Sin(p), Mathf.Cos(y) * c);
            }

            private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

            private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

            // Picked up by harmony.PatchAll(). If the game method is ever renamed, Prepare() returns false
            // and Harmony skips only this patch instead of failing the whole PatchAll().
            [HarmonyPatch]
            private static class CharacterSyncerPatch
            {
                private static System.Reflection.MethodBase _target;

                [HarmonyPrepare]
                private static bool Prepare()
                {
                    if (_target == null)
                        _target = AccessTools.Method(typeof(CharacterSyncer), "GetDataToWrite");
                    if (_target == null)
                        LogMissingTarget();
                    return _target != null;
                }

                [HarmonyTargetMethod]
                private static System.Reflection.MethodBase TargetMethod() => _target;

                [HarmonyPostfix]
                private static void Postfix(CharacterSyncer __instance, ref CharacterSyncData __result)
                {
                    Rewrite(__instance, ref __result);
                }
            }
        }
    }
}
