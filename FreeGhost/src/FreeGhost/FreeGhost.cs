using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using System;
using System.ComponentModel.Design;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
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
        static ConfigEntry<float> otherDown;
        static ConfigEntry<float> otherForward;
        public static Vector3 desiredPosition;
        public static Quaternion desiredRotation;
        public static GameObject visualProp = null;
        public static PhysicsSyncer syncer = null;
        internal static bool freecamActive = false;
        public static Item visualItem;
        private static bool propisAntiGravity = false;
        private static bool propIsKinematic = true;
        internal static FreecamController controller;

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
            otherDown = config.Bind("FreeGhost", "other down", 0.7f,
                new ConfigDescription("Prop Down",
                new AcceptableValueRange<float>(-50f, 50f)));
            otherForward = config.Bind("FreeGhost", "other forward", 1f,
                new ConfigDescription("Prop Forward",
                new AcceptableValueRange<float>(-50f, 50f)));

            GameObject managerObj = new GameObject("GlobalFreecamManager");
            UnityEngine.Object.DontDestroyOnLoad(managerObj);
            managerObj.AddComponent<GlobalFreecamManager>();
        }

        [HarmonyPatch]
        public static class PlayerGhostPatch
        {
            private static float moveSpeed = 5f;
            private static float lookSpeed = 2f;
            internal static float rotationX = 0f;
            internal static float rotationY = 0f;
            static bool isFlipped = false;

            internal static bool subscribedToRenderHooks = false;

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

            [HarmonyPatch(typeof(MainCamera), "Update")]
            [HarmonyPostfix]
            public static void Postfix_MainCameraLateUpdate(MainCamera __instance)
            {
                if (!freecamActive || __instance.cam == null || !enableFreeGhost.Value)
                    return;
                if (GUIManager.instance == null || GUIManager.instance.windowBlockingInput)
                    return;
                if (controller != null && controller.linkedVisualProp != null && !controller.linkedVisualProp.activeSelf)
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
                    if (controller != null && controller.spectatingCharacter != null)
                    {
                        if (Vector3.Distance(desiredPosition, controller.spectatingCharacter.Center) > 15)
                            desiredPosition = controller.spectatingCharacter.Center + Vector3.up * 10;
                    }
                }
                if (Input.GetKey(KeyCode.Escape))
                {
                    if (Character.localCharacter == null)
                    {
                        var pauseMenu = GameObject.Find("GAME/GUIManager/PauseMenu");
                        if (pauseMenu != null)
                            pauseMenu.SetActive(!pauseMenu.activeSelf);
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
                if (controller != null && controller.GetComponent<PlayerGhost>() is PlayerGhost localGhost)
                {
                    SyncGhostLocation.TryEncode(localGhost, desiredPosition);
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

                    if (visualItem != null)
                        targetPosition = desiredPosition + forward * visualPropForward.Value + Vector3.down * visualPropDown.Value;
                    else
                    {
                        if (syncer != null)
                            targetPosition = desiredPosition + forward * StatueForward.Value + Vector3.down * StatueDown.Value;
                        else
                            targetPosition = desiredPosition + forward * otherForward.Value + Vector3.down * otherDown.Value;
                    }

                    visualProp.transform.rotation = targetRotation;
                    Vector3 localOffset = GetLocalCenterOffset(visualProp);
                    Vector3 worldOffset = targetRotation * localOffset;
                    Vector3 finalPos = targetPosition - worldOffset;
                    visualProp.transform.position = finalPos;

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

            internal static void OnCameraPreCull(Camera cam) => ApplyDesiredTransform(cam);
            internal static void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam) => ApplyDesiredTransform(cam);

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
                if (!freecamActive || !enableFreeGhost.Value || controller == null) return;
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

        public class GlobalFreecamManager : MonoBehaviour
        {
            void Update()
            {
                if (!enableFreeGhost.Value) return;

                // Restrict fallback camera injection to specific scene types
                string activeSceneName = SceneManager.GetActiveScene().name;
                if (activeSceneName != "Airport" && !activeSceneName.StartsWith("Level_")) return;

                if (Character.localCharacter == null)
                {
                    if (MainCamera.instance == null || MainCamera.instance.cam == null)
                    {
                        GameObject camObj = new GameObject("FreeGhost_FallbackCamera");
                        Camera cam = camObj.AddComponent<Camera>();
                        camObj.AddComponent<AudioListener>();

                        MainCamera mainCam = camObj.AddComponent<MainCamera>();
                        mainCam.cam = cam;

                        if (MainCamera.instance == null)
                            MainCamera.instance = mainCam;

                        desiredPosition = new Vector3(0, 10, 0);
                        desiredRotation = Quaternion.identity;
                        freecamActive = true;

                        if (!PlayerGhostPatch.subscribedToRenderHooks)
                        {
                            Camera.onPreCull += PlayerGhostPatch.OnCameraPreCull;
                            RenderPipelineManager.beginCameraRendering += PlayerGhostPatch.OnBeginCameraRendering;
                            PlayerGhostPatch.subscribedToRenderHooks = true;
                        }
                    }
                    else if (!freecamActive)
                    {
                        freecamActive = true;
                        Transform camTransform = MainCamera.instance.cam.transform;
                        desiredPosition = camTransform.position;
                        desiredRotation = camTransform.rotation;
                        PlayerGhostPatch.rotationX = camTransform.eulerAngles.x;
                        PlayerGhostPatch.rotationY = camTransform.eulerAngles.y;
                    }
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

            Rigidbody hipRb = item.GetComponentInChildren<Rigidbody>();
            if (hipRb == null)
            {
                Transform hipTransform = item.transform.Find("Scout/Armature/Hip (RIGIDBODY)");
                if (hipTransform != null)
                    hipRb = hipTransform.gameObject.AddComponent<Rigidbody>();
                else
                    hipRb = item.AddComponent<Rigidbody>();
            }

            hipRb.isKinematic = false;
            hipRb.useGravity = false;
            hipRb.linearVelocity = Vector3.zero;
            hipRb.angularVelocity = Vector3.zero;

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

            if (controller != null) controller.linkedVisualProp = visualProp;
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
                visualItem.SetKinematicNetworked(false);
                if (visualItem.GetComponent<PhotonView>() != null && visualItem.GetComponent<PhotonView>().IsMine)
                    visualItem.GetComponent<PhotonView>().TransferOwnership(PhotonNetwork.MasterClient);
            }

            if (!propisAntiGravity && visualProp != null)
                UnityEngine.Object.Destroy(visualProp.GetComponent<Antigrav>());

            visualProp = null;
            propisAntiGravity = false;
            visualItem = null;
            syncer = null;
            if (controller != null) controller.linkedVisualProp = null;
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

        public static class SyncGhostLocation
        {
            public const float MaxSyncDistance = 500f;
            public static readonly Vector3 GhostUpOffset = new Vector3(0f, 0.5f, 0f);

            public static bool TryEncode(PlayerGhost ghost, Vector3 desiredPosition)
            {
                if (ghost == null || ghost.m_owner == null || ghost.m_target == null)
                    return false;

                if (ghost.m_owner.photonView == null || !ghost.m_owner.photonView.IsMine)
                    return false;

                Vector3 targetOrigin = ghost.m_target.Center + GhostUpOffset;
                Vector3 targetOffset = desiredPosition - targetOrigin;
                float distance = targetOffset.magnitude;

                if (float.IsNaN(distance) || float.IsInfinity(distance))
                    return false;

                if (distance <= 0.0001f)
                {
                    ghost.m_owner.data.lookDirection = Vector3.forward;
                    ghost.m_owner.data.spectateZoom = 0f;
                    return true;
                }

                float clampedDistance = Mathf.Min(distance, MaxSyncDistance);
                Vector3 lookDirection = -targetOffset.normalized;

                ghost.m_owner.data.lookDirection = lookDirection;
                ghost.m_owner.data.spectateZoom = clampedDistance;

                return true;
            }

            public static Vector3 Decode(Vector3 targetCenter, Vector3 lookDirection, float spectateZoom)
            {
                Vector3 origin = targetCenter + GhostUpOffset;
                return origin - (lookDirection.normalized * spectateZoom);
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