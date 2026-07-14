using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using pworld.Scripts.PPhys;
using Sirenix.Utilities;
using System;
using System.Collections;
using System.Diagnostics.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using static FreeGhost.FreeGhost_Functions.RightClickHandler;

namespace FreeGhost
{
    public class FreeGhost
    {
        static ConfigEntry<bool> enableFreeGhost;
        static ConfigEntry<bool> giveVisualProp;
        static ConfigEntry<string> visualItemName;
        static ConfigEntry<float> visualPropDown;
        static ConfigEntry<float> visualPropForward;
        public static Vector3 desiredPosition;
        public static Quaternion desiredRotation;
        public static GameObject visualProp = null;
        private static bool freecamActive = false;
        public static Item visualItem;
        private static bool propisAntiGravity = false;
        private static bool propIsKinematic = true;
        private static FreecamController controller;

        public static void Binds(ConfigFile config)
        {
            enableFreeGhost = config.Bind("FreeGhost", "enable free ghost", true);
            giveVisualProp = config.Bind("FreeGhost", "give visual prop", false);
            visualItemName = config.Bind("FreeGhost", "visual item name", "0_Items/Binoculars_Prop");
            visualPropDown = config.Bind("FreeGhost", "visual prop down", 0f,
                new ConfigDescription("Prop Down",
                new AcceptableValueRange<float>(-5f, 5f)));
            visualPropForward = config.Bind("FreeGhost", "visual prop forward", 0f,
                new ConfigDescription("Prop Forward",
                new AcceptableValueRange<float>(-5f, 5f)));
        }
        [HarmonyPatch]
        public static class PlayerGhostPatch
        {
            private static float moveSpeed = 5f;
            private static float lookSpeed = 2f;
            private static float rotationX = 0f;
            private static float rotationY = 0f;

            
            // The transform we WANT the camera at, computed once per frame in
            // Postfix_MainCameraLateUpdate. The two render-pipeline hooks below
            // just re-apply these values - they don't recompute input - so we
            // never double-apply a mouse/movement delta in the same frame.

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
                    if (giveVisualProp.Value)
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
                        else propisAntiGravity = true;
                        propIsKinematic = true;
                        if (visualItem != null)
                            visualItem.SetKinematicNetworked(true);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Sync Error: {e.Message}");
                }

                controller = __instance.gameObject.AddComponent<FreecamController>();
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
                PhotonView pv = __instance.GetComponent<PhotonView>();
                if (pv != null && !pv.IsMine) return true;
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
                if (GUIManager.instance == null || GUIManager.instance.windowBlockingInput)
                    return;
                if (controller.linkedVisualProp == null || !controller.linkedVisualProp.activeSelf)
                    DropItem();
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
                if (Input.GetKey(KeyCode.F)) //focus back on player
                {
                    if (controller.spectatingCharacter != null)
                    {
                        if (Vector3.Distance(desiredPosition, controller.spectatingCharacter.Center) > 15)
                            desiredPosition = controller.spectatingCharacter.Center + Vector3.up * 10;
                    }
                }
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
                    if (Input.GetKey(KeyCode.Q)) //drop
                    {
                        DropItem();
                        return;
                    }
                    if (Input.GetMouseButtonDown(0))
                        visualItem.FinishCastPrimary();
                    if (Input.GetMouseButtonDown(1))
                    {
                        HandleRightClick();
                    }
                    visualProp.transform.position = desiredPosition + forward * visualPropForward.Value + Vector3.down * visualPropDown.Value;
                    visualProp.transform.rotation = desiredRotation;
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    HandleRightClick();   
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
            

            //onSwitchCharacter
            [HarmonyPatch(typeof(PlayerGhost), "RPCA_SetTarget")]
            [HarmonyPostfix]
            private static void OnSwitchPlayer(PlayerGhost __instance,  PhotonView t)
            {
                if (!freecamActive || !enableFreeGhost.Value) return;
                PhotonView pv = __instance.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                {
                    Character c = t.GetComponent<Character>();
                    controller.spectatingCharacter = c;
                    if (Vector3.Distance(desiredPosition, c.Center)>50)
                        desiredPosition = c.Center + Vector3.up * 10;
                }
            }
            
            
        }
        public static void PossessItem(Item item)
        {
            PhotonView pv = item.GetComponent<PhotonView>();

            if (pv == null) return;
            if (!pv.IsMine)
                pv.RequestOwnership();

            visualProp = item.gameObject;
            visualItem = item;
            if (visualProp.GetComponent<Antigrav>() == null)
            {
                visualProp.AddComponent<Antigrav>();
                propisAntiGravity = false;
            }
            else propisAntiGravity = true;
            propIsKinematic = true;
            if (visualItem != null)
                visualItem.SetKinematicNetworked(true);
            controller.linkedVisualProp = visualProp;
        }
        public static void DropItem()
        {
            if (visualItem != null)
                visualItem.SetKinematic(false);
            if (!propisAntiGravity && visualProp != null)
                UnityEngine.Object.Destroy(visualProp.GetComponent<Antigrav>());
            visualProp = null;
            propisAntiGravity = false;
            visualItem = null;
            controller.linkedVisualProp = null;
        }
    }

    public class FreecamController : MonoBehaviour
    {
        public GameObject linkedVisualProp;
        public Character spectatingCharacter;
        public Action onDestroyed;

        private void OnDestroy()
        {
            onDestroyed?.Invoke();
            if (linkedVisualProp != null && PhotonNetwork.IsConnected && PhotonNetwork.LocalPlayer != null)
                PhotonNetwork.Destroy(linkedVisualProp);
        }
    }
}