using UnityEngine;

namespace ShootingEditor2D
{
    public interface IInteractable
    {
        bool IsInteractable { get; }
        (bool success, string result) Interact(GameObject chara);

        (bool success, string result) Select(GameObject chara, int selection);

        (bool success, string result) TextInput(GameObject chara, string inputText);
    }
}
