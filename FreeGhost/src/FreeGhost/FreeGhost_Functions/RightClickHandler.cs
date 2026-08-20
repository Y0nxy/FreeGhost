using Peak;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static FreeGhost.FreeGhost;

namespace FreeGhost.FreeGhost_Functions
{
    public static class RightClickHandler
    {
        public static float maxDistance = 20f;
        public static void HandleRightClick()
        {
            //Plugin.log("RightClicked!");
            if (visualProp != null)
            {
                if (visualItem == null) return;
                if (visualItem.canUseOnFriend)
                {
                    Plugin.log("canUseOnFriend");
                    RayCast(maxDistance, true);
                }
                else
                {
                    bool isBuilding = HandleBuilding();
                    if (!isBuilding)
                        RayCast(maxDistance, false);
                }
                visualItem.FinishCastSecondary();
            }
            else
            {
                RayCast(maxDistance, false);
            }
        }

        public static void RayCast(float maxDistance, bool tryFindCharacter)
        {
            EnsureGhostBallColliders();
            Plugin.log($"RayCast! maxDist: {maxDistance}, tryfinchar: {tryFindCharacter}");
            Ray ray = new Ray(FreeGhost.desiredPosition, FreeGhost.desiredRotation * Vector3.forward);
            RaycastHit rcHit;
            if (!tryFindCharacter && Physics.Raycast(ray, out rcHit, maxDistance))
            {
                RaycastHit[] hits = Physics.SphereCastAll(ray, 0.2f, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

                // Sort hits so closest objects are evaluated first
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

                foreach (RaycastHit raycastHit in hits)
                {
                    GameObject hit = raycastHit.collider.gameObject;
                    Plugin.log("Checking object: " + hit.name);

                    // 1. Item Check
                    Item parentItem = hit.GetComponentInParent<Item>();
                    if (parentItem != null)
                    {
                        Plugin.log("Trying to possess item: " + parentItem.name);
                        FreeGhost.PossessItem(parentItem.gameObject);
                        return;
                    }

                    // 2. Luggage Check
                    Luggage luggage = hit.GetComponentInParent<Luggage>();
                    if (luggage != null)
                    {
                        if (luggage.state != Luggage.LuggageState.Open)
                        {
                            luggage.photonView.RPC("OpenLuggageRPC", RpcTarget.All, new object[] { true });
                            Plugin.log("Opened luggage: " + luggage.name);
                            return;
                        }
                    }

                    // 3. PhysicsSyncer Check
                    var phySync = hit.GetComponentInParent<PhysicsSyncer>();
                    if (phySync != null)
                    {
                        FreeGhost.PossessItem(phySync.gameObject);
                        Plugin.log("Trying to possess statue");
                        return;
                    }

                    // 4. Photon/Syncer Fallbacks (Fixed assignment logic)
                    Component otherObj = (Component)hit.GetComponentInParent<PhotonTransformView>()
                                      ?? (Component)hit.GetComponentInParent<PositionSyncer>()
                                      ?? (Component)hit.GetComponentInParent<PhotonRigidbodyView>();

                    if (otherObj != null)
                    {
                        FreeGhost.PossessItem(otherObj.gameObject);
                        Plugin.log("Trying to possess other: " + otherObj.name);
                        return;
                    }
                }
            }
            else if (Physics.Raycast(ray, out rcHit, maxDistance, LayerMask.GetMask("Character")))
            {
                CharacterInteractible characterInteract = rcHit.collider.GetComponentInParent<CharacterInteractible>();
                if (characterInteract != null)
                    Interaction.instance.bestCharacter = characterInteract;
                Plugin.log($"Right Clicked on: {Interaction.instance.bestCharacter.name}");
            }
        }
        public static bool HandleBuilding()
        {
            GameObject prop = FreeGhost.visualProp;
            Constructable construct = prop.GetComponent<Constructable>();
            if (construct != null)
            {
                construct.photonView.RPC("CreatePrefabRPC", RpcTarget.AllBuffered, new object[]
                {
                                    //construct.currentPreview.transform.position,
                                    //construct.currentPreview.transform.rotation
                                    prop.transform.position,
                                    FreeGhost.desiredRotation
                });
                PhotonNetwork.Destroy(prop.gameObject);
                FreeGhost.DropItem();
                return true;
            }
            return false;
        }
        public static void EnsureGhostBallColliders()
        {
            // 1. Include inactive objects in the search
            GhostBall[] ghostBalls = UnityEngine.Object.FindObjectsByType<GhostBall>(FindObjectsSortMode.None);

            Plugin.log($"Found {ghostBalls.Length} GhostBall instances in scene.");

            foreach (GhostBall ball in ghostBalls)
            {
                if (ball.GetComponent<SphereCollider>() != null) continue;
                SphereCollider col = ball.gameObject.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.center = new Vector3(0, 0.1f, 0);
                col.radius = 0.7f;
                Plugin.log($"Successfully added SphereCollider to GhostBall: {ball.name}");
            }
        }
        public static void EnsureTornadoColliders()
        {
            Tornado[] Tornados = UnityEngine.Object.FindObjectsByType<Tornado>(FindObjectsSortMode.None);
            Plugin.log($"Found {Tornados.Length} Tornados instances in scene.");

            foreach (Tornado tornado in Tornados)
            {
                if (tornado.GetComponent<BoxCollider>() != null) continue;
                BoxCollider col = tornado.gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = new Vector3(0, 80, 0);
                col.size = new Vector3(70, 150, 70);
                Plugin.log($"Successfully added BoxCollider to Tornado: {tornado.name}");
            }
        }
    }
}
