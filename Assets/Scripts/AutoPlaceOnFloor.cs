using UnityEngine;
using Meta.XR.MRUtilityKit;

public class RepositionToFloor : MonoBehaviour
{
    [Header("The Object to Move")]
    public GameObject objectToMove;

    void Start()
    {
        // If you attached this directly to the object, just use 'this.gameObject'
        if (objectToMove == null) objectToMove = this.gameObject;

        // Subscribe to the event that triggers when the room scan is ready
        if (MRUK.Instance != null)
        {
            MRUK.Instance.SceneLoadedEvent.AddListener(OnRoomLoaded);
        }
    }

    private void OnRoomLoaded()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();

        // Try to get the floor anchor
        MRUKAnchor floor = room.FloorAnchor;

        if (floor != null)
        {
            // 1. Get the floor's center position
            Vector3 floorPos = floor.transform.position;

            // 2. Set the object's position
            // We use the floor's Y-height, but keep it in front of the player
            Vector3 targetPosition = Camera.main.transform.position + (Camera.main.transform.forward * 1.5f);
            targetPosition.y = floorPos.y;

            objectToMove.transform.position = targetPosition;

            // 3. Make it face the player
            Vector3 lookTarget = Camera.main.transform.position;
            lookTarget.y = objectToMove.transform.position.y; // Keep rotation level
            objectToMove.transform.LookAt(lookTarget);

            Debug.Log("Object repositioned to floor!");
        }
    }
}