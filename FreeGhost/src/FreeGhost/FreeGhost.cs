using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using System;
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
        static ConfigEntry<float> StatueDown;
        static ConfigEntry<float> StatueForward;
        public static Vector3 desiredPosition;
        public static Quaternion desiredRotation;
        public static GameObject visualProp = null;
        public static PhysicsSyncer syncer = null;
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
            visualPropDown = config.Bind("FreeGhost", "visual prop down", 0.7f,
                new ConfigDescription("Prop Down",
                new AcceptableValueRange<float>(-5f, 5f)));
            visualPropForward = config.Bind("FreeGhost", "visual prop forward", 1f,
                new ConfigDescription("Prop Forward",
                new AcceptableValueRange<float>(-5f, 5f)));
            StatueDown = config.Bind("FreeGhost", "statue down", 0.7f,
                new ConfigDescription("Prop Down",
                new AcceptableValueRange<float>(-5f, 5f)));
            StatueForward = config.Bind("FreeGhost", "statue forward", 1f,
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
            static bool isFlipped = false;

            private static bool subscribedToRenderHooks = false;

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
                isFlipped = false;
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

                if (!subscribedToRenderHooks)
                {
                    Camera.onPreCull += OnCameraPreCull;
                    RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                    subscribedToRenderHooks = true;
                    Plugin.Log.LogInfo("FreeGhost: render hooks subscribed");
                }

                freecamActive = true;
            }

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

                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                rotationY += mouseX * lookSpeed;
                rotationX -= mouseY * lookSpeed;
                rotationX = Mathf.Clamp(rotationX, -90f, 90f);
                desiredRotation = Quaternion.Euler(rotationX, rotationY, 0f);

                Vector3 forward = desiredRotation * Vector3.forward;
                Vector3 right = desiredRotation * Vector3.right;

                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += forward;
                if (Input.GetKey(KeyCode.S)) move -= forward;
                if (Input.GetKey(KeyCode.A)) move -= right;
                if (Input.GetKey(KeyCode.D)) move += right;
                if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
                if (Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;
                if (Input.GetKey(KeyCode.F))
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

                __instance.cam.transform.position = desiredPosition;
                __instance.cam.transform.rotation = desiredRotation;

                bool isRotating = Mathf.Abs(mouseX) > 0.001f || Mathf.Abs(mouseY) > 0.001f;
                bool isChanging = isMoving || isRotating;

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
                    if (Input.GetKeyDown(KeyCode.Q))
                    {
                        DropItem();
                        return;
                    }
                    if (Input.GetMouseButtonDown(0) && visualItem != null)
                    {
                        visualItem.FinishCastPrimary();
                    }
                    if (Input.GetMouseButtonDown(1))
                    {
                        HandleRightClick();
                    }
                    if (Input.GetKeyDown(KeyCode.R))
                    {
                        isFlipped = !isFlipped;
                    }

                    Quaternion targetRotation = isFlipped ? desiredRotation * Quaternion.Euler(0, 180, 0) : desiredRotation;
                    Vector3 targetPosition;
                    if (syncer == null)
                        targetPosition = desiredPosition + forward * visualPropForward.Value + Vector3.down * visualPropDown.Value;
                    else
                        targetPosition = desiredPosition + forward * StatueForward.Value + Vector3.down * StatueDown.Value;

                    // 1. Rotate root object
                    visualProp.transform.rotation = targetRotation;

                    // 2. Derive offset relative to child Rigidbody position
                    Vector3 localOffset = GetLocalCenterOffset(visualProp);
                    Vector3 worldOffset = targetRotation * localOffset;

                    // 3. Position root so child Rigidbody centers on targetPosition
                    Vector3 finalPos = targetPosition - worldOffset;
                    visualProp.transform.position = finalPos;

                    // 4. Update child Rigidbody position/rotation for PhysicsSyncer
                    Rigidbody hipRb = visualProp.GetComponentInChildren<Rigidbody>();
                    if (hipRb != null)
                    {
                        hipRb.linearVelocity = Vector3.zero;
                        hipRb.angularVelocity = Vector3.zero;
                        hipRb.position = finalPos;
                        hipRb.rotation = targetRotation;
                    }
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

            [HarmonyPatch(typeof(PlayerGhost), "RPCA_SetTarget")]
            [HarmonyPostfix]
            private static void OnSwitchPlayer(PlayerGhost __instance, PhotonView t)
            {
                if (!freecamActive || !enableFreeGhost.Value) return;
                PhotonView pv = __instance.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                {
                    Character c = t.GetComponent<Character>();
                    controller.spectatingCharacter = c;
                    if (Vector3.Distance(desiredPosition, c.Center) > 50)
                        desiredPosition = c.Center + Vector3.up * 10;
                }
            }
        }

        public static void PossessItem(GameObject item)
        {
            PhotonView pv = item.GetComponent<PhotonView>();
            if (pv == null) return;
            EnsureOwnership(pv);

            visualProp = item;
            visualItem = item.GetComponent<Item>();

            // Find existing child Rigidbody or attach if missing
            Rigidbody hipRb = item.GetComponentInChildren<Rigidbody>();
            if (hipRb == null)
            {
                Transform hipTransform = item.transform.Find("Scout/Armature/Hip (RIGIDBODY)");
                if (hipTransform != null)
                    hipRb = hipTransform.gameObject.AddComponent<Rigidbody>();
                else
                    hipRb = item.AddComponent<Rigidbody>();
            }

            // Keep non-kinematic for PUN sync, but disable gravity for levitation
            hipRb.isKinematic = false;
            hipRb.useGravity = false;
            hipRb.linearVelocity = Vector3.zero;
            hipRb.angularVelocity = Vector3.zero;

            // Bind active Rigidbody reference to PhysicsSyncer
            syncer = item.GetComponentInParent<PhysicsSyncer>();
            if (syncer != null)
            {
                syncer.rig = hipRb;
                syncer.shouldSync = true;
                syncer.ForceSyncForFrames(30);
            }

            if (visualProp.GetComponent<Antigrav>() == null && syncer == null)
            {
                visualProp.AddComponent<Antigrav>();
                propisAntiGravity = false;
            }
            else propisAntiGravity = true;

            if (visualItem != null)
            {
                visualItem.SetKinematicNetworked(false);
                propIsKinematic = false;
            }

            controller.linkedVisualProp = visualProp;
        }

        public static void DropItem()
        {
            if (visualProp != null)
            {
                Rigidbody hipRb = visualProp.GetComponentInChildren<Rigidbody>();
                if (hipRb != null)
                {
                    hipRb.useGravity = true;
                }
            }

            if (visualItem != null)
            {
                visualItem.SetKinematic(false);
                if (visualItem.GetComponent<PhotonView>() != null && visualItem.GetComponent<PhotonView>().IsMine)
                    visualItem.GetComponent<PhotonView>().TransferOwnership(PhotonNetwork.MasterClient);
            }

            if (!propisAntiGravity && visualProp != null)
                UnityEngine.Object.Destroy(visualProp.GetComponent<Antigrav>());

            visualProp = null;
            propisAntiGravity = false;
            visualItem = null;
            syncer = null;
            controller.linkedVisualProp = null;
        }

        private static Vector3 GetLocalCenterOffset(GameObject root)
        {
            Rigidbody childRb = root.GetComponentInChildren<Rigidbody>();
            if (childRb != null)
            {
                return root.transform.InverseTransformPoint(childRb.transform.position);
            }

            return Vector3.zero;
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
                {
                    DropItem();
                }
            }
        }

        private static void EnsureOwnership(PhotonView view)
        {
            if (view == null || view.IsMine) return;
            if (view.OwnershipTransfer == OwnershipOption.Fixed)
            {
                view.OwnershipTransfer = OwnershipOption.Takeover;
            }
            view.RequestOwnership();
            view.TransferOwnership(PhotonNetwork.LocalPlayer);
        }
    }
}