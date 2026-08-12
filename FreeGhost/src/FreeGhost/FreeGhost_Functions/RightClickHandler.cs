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
        public static float maxDistance = 10f;
        public static void HandleRightClick()
        {
            Plugin.log("RightClicked!");
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
            Plugin.log($"RayCast! maxDist: {maxDistance}, tryfinchar: {tryFindCharacter}");
            Ray ray = new Ray(FreeGhost.desiredPosition, FreeGhost.desiredRotation * Vector3.forward);
            RaycastHit raycastHit;
            if (!tryFindCharacter && Physics.Raycast(ray, out raycastHit, maxDistance))
            {
                //Item
                GameObject hit = raycastHit.collider.gameObject;
                Plugin.log("found object: " + hit.name);
                Item parentItem = hit.GetComponentInParent<Item>();
                if (parentItem != null)
                {
                    Plugin.log("Trying to possess item: " + hit.name);
                    FreeGhost.PossessItem(parentItem.gameObject);
                    return;
                }
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
                PhysicsSyncer phySync = hit.GetComponentInParent<PhysicsSyncer>();
                if (phySync != null)
                {
                    FreeGhost.PossessItem(phySync.gameObject);
                    //FreeGhost.setCameraObj(phySync.GetComponentInChildren<CustomizationRefs>().gameObject); //main mesh should be in the center
                    Plugin.log("Trying to posses statue");
                    return;
                }
            }
            else if (Physics.Raycast(ray, out raycastHit, maxDistance, LayerMask.GetMask("Character")))
            {
                CharacterInteractible characterInteract = raycastHit.collider.GetComponentInParent<CharacterInteractible>();
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
    }
}
