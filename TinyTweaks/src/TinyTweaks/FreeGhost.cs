using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TinyTweaks
{
    public class FreeGhost
    {
        static ConfigEntry<bool> enableFreeGhost;
        static ConfigEntry<string> visualItemName;

        public static void Binds(ConfigFile config)
        {
            enableFreeGhost = config.Bind("FreeGhost", "enable free ghost", true);
            visualItemName = config.Bind("FreeGhost", "visual item name", "0_Items/Binoculars_Prop");
        }
        [HarmonyPatch]
        public static class PlayerGhostPatch
        {
            private static float moveSpeed = 5f;
            private static float lookSpeed = 2f;
            private static float rotationX = 0f;
            private static float rotationY = 0f;

            private static bool freecamActive = false;
            private static GameObject visualProp;
            private static Item visualItem;
            private static bool propIsKinematic = true;

            // The transform we WANT the camera at, computed once per frame in
            // Postfix_MainCameraLateUpdate. The two render-pipeline hooks below
            // just re-apply these values - they don't recompute input - so we
            // never double-apply a mouse/movement delta in the same frame.
            private static Vector3 desiredPosition;
            private static Quaternion desiredRotation;

            private static bool subscribedToRenderHooks = false;

            // PlayerGhost.RPCA_InitGhost(PhotonView character, PhotonView t)
            [HarmonyPatch(typeof(PlayerGhost), "RPCA_InitGhost")]
            [HarmonyPostfix]
            public static void Postfix_InitGhost(PlayerGhost __instance, PhotonView character, PhotonView t)
            {
                if (!enableFreeGhost.Value)
                    return;
                if (character == null || !character.IsMine)
                    return;

                if (MainCamera.instance == null || MainCamera.instance.cam == null)
                {
                    Plugin.Log.LogError("FreeGhost: MainCamera.instance.cam was null on init");
                    return;
                }

                Transform camTransform = MainCamera.instance.cam.transform;
                rotationY = camTransform.eulerAngles.y;
                rotationX = camTransform.eulerAngles.x;
                desiredPosition = camTransform.position;
                desiredRotation = camTransform.rotation;

                try
                {
                    visualProp = PhotonNetwork.Instantiate(
                        visualItemName.Value,
                        camTransform.position,
                        camTransform.rotation,
                        0, null);

                    // Kinematic while stationary so physics doesn't fight our
                    // manual transform writes; flipped to non-kinematic while
                    // moving/rotating in Postfix_MainCameraLateUpdate, since a
                    // kinematic rigidbody's movement isn't what gets networked.
                    visualItem = visualProp.GetComponent<Item>();
                    if (visualProp.GetComponent<Antigrav>() == null)
                        visualProp.AddComponent<Antigrav>();
                    propIsKinematic = true;
                    if (visualItem != null)
                        visualItem.SetKinematicNetworked(true);

                    // Hide it from our OWN camera only - it spawns right at the
                    // camera position so it'd otherwise block our view. Other
                    // clients have their own separately-instantiated copy of
                    // this networked object, so disabling renderers here is
                    // purely local and doesn't need to be synced.
                    foreach (Renderer r in visualProp.GetComponentsInChildren<Renderer>())
                        r.enabled = false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Sync Error: {e.Message}");
                }

                FreecamController controller = __instance.gameObject.AddComponent<FreecamController>();
                controller.linkedVisualProp = visualProp;
                controller.onDestroyed = () => freecamActive = false;

                // Two last-resort enforcement hooks, covering both rendering
                // paths Unity supports:
                //  - Camera.onPreCull fires after ALL LateUpdates, right before
                //    that camera culls/renders - but ONLY on the built-in render
                //    pipeline.
                //  - RenderPipelineManager.beginCameraRendering is the SRP
                //    (URP/HDRP) equivalent; onPreCull is never invoked there.
                // Only one of these will actually fire depending on the
                // project's pipeline, so subscribing to both costs nothing and
                // removes the guesswork.
                if (!subscribedToRenderHooks)
                {
                    Camera.onPreCull += OnCameraPreCull;
                    RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                    subscribedToRenderHooks = true;
                    Plugin.Log.LogInfo("FreeGhost: render hooks subscribed");
                }

                freecamActive = true;
            }

            // PlayerGhost.Update() normally re-points the ghost body at the
            // camera every frame using m_target. Doesn't affect the real
            // camera, but it's wasted work once freecam is active, so skip it.
            [HarmonyPatch(typeof(PlayerGhost), "Update")]
            [HarmonyPrefix]
            public static bool Prefix_GhostUpdate(PlayerGhost __instance)
            {
                if (!enableFreeGhost.Value)
                    return true;

                return !freecamActive;
            }

            // Read input and integrate movement ONCE per frame.
            [HarmonyPatch(typeof(MainCamera), "LateUpdate")]
            [HarmonyPostfix]
            public static void Postfix_MainCameraLateUpdate(MainCamera __instance)
            {
                if (!freecamActive || __instance.cam == null || !enableFreeGhost.Value)
                    return;

                // Mouse look
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                rotationY += mouseX * lookSpeed;
                rotationX -= mouseY * lookSpeed;
                rotationX = Mathf.Clamp(rotationX, -90f, 90f);
                desiredRotation = Quaternion.Euler(rotationX, rotationY, 0f);

                Vector3 forward = desiredRotation * Vector3.forward;
                Vector3 right = desiredRotation * Vector3.right;

                // Flight movement
                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += forward;
                if (Input.GetKey(KeyCode.S)) move -= forward;
                if (Input.GetKey(KeyCode.A)) move -= right;
                if (Input.GetKey(KeyCode.D)) move += right;
                if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
                if (Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;

                bool isMoving = move != Vector3.zero;
                if (isMoving)
                {
                    float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * 3f : moveSpeed;
                    desiredPosition += move.normalized * speed * Time.deltaTime;
                }

                // Apply immediately too, in case neither render hook fires for
                // some reason - better a slightly-early write than no write.
                __instance.cam.transform.position = desiredPosition;
                __instance.cam.transform.rotation = desiredRotation;

                // Non-kinematic whenever position OR rotation is actively
                // changing - a small deadzone avoids toggling on mouse jitter.
                bool isRotating = Mathf.Abs(mouseX) > 0.001f || Mathf.Abs(mouseY) > 0.001f;
                bool isChanging = isMoving || isRotating;

                // Only fire the RPC on state transitions, not every frame.
                if (visualItem != null)
                {
                    if (isChanging && propIsKinematic)
                    {
                        visualItem.SetKinematicNetworked(false);
                        propIsKinematic = false;
                    }
                    else if (!isChanging && !propIsKinematic)
                    {
                        visualItem.SetKinematicNetworked(true);
                        propIsKinematic = true;
                    }
                }

                if (visualProp != null)
                {
                    visualProp.transform.position = desiredPosition;
                    visualProp.transform.rotation = desiredRotation;
                }
            }

            private static void OnCameraPreCull(Camera cam) => ApplyDesiredTransform(cam);
            private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam) => ApplyDesiredTransform(cam);

            private static void ApplyDesiredTransform(Camera cam)
            {
                if (!freecamActive || cam == null || MainCamera.instance == null || cam != MainCamera.instance.cam || !enableFreeGhost.Value)
                    return;

                cam.transform.position = desiredPosition;
                cam.transform.rotation = desiredRotation;
            }
        }
    }

    public class FreecamController : MonoBehaviour
    {
        public GameObject linkedVisualProp;
        public Action onDestroyed;

        private void OnDestroy()
        {
            onDestroyed?.Invoke();
            if (linkedVisualProp != null && PhotonNetwork.IsConnected && PhotonNetwork.LocalPlayer != null)
                PhotonNetwork.Destroy(linkedVisualProp);
        }
    }
}