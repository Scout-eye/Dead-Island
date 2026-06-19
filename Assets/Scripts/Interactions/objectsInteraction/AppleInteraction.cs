using UnityEngine;

namespace DefaultNamespace
{
    public class AppleInteraction : MonoBehaviour, IInteractable
    {
        public string InteractionText => interactionText;
        [SerializeField] string interactionText;

        public void Interact()
        {
            Debug.Log("Interacting with Apple");
        }
    }
}
