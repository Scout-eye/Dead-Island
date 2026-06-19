using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    public class InteractionController : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] TextMeshProUGUI interactionText;
        [SerializeField] float interactionDistance = 5f;

        IInteractable currentInteractable;

        public void Update()
        {
            UpdateCurrentInteractable();
            UpdateInteractionText();
            CheckForInteractableInput();
        }

        void UpdateCurrentInteractable()
        {
            var ray = playerCamera.ViewportPointToRay(new Vector2(0.5f, 0.5f));
            Physics.Raycast(ray, out RaycastHit hit, interactionDistance);
            currentInteractable = hit.collider?.GetComponent<IInteractable>();
        }
        void UpdateInteractionText()
        {
            if (currentInteractable == null)
            {
                interactionText.text = string.Empty;
                return;
            }

            interactionText.text = currentInteractable.InteractionText;
        }
        void CheckForInteractableInput()
        {
            if (Keyboard.current.eKey.wasPressedThisFrame && currentInteractable != null)
            {
                currentInteractable.Interact();
            }
        }
    }
}