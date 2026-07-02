using UnityEngine;

namespace IndependentAgentProject
{
    public interface IInteractable
    {
        bool IsInteractable { get; } // 注意：该属性不能设置为动态数值，否则在动作序列校验时，可能会前后不一致
        (bool success, string result) Interact(GameObject chara);

        (bool success, string result) Select(GameObject chara, int selection);

        (bool success, string result) TextInput(GameObject chara, string inputText);
    }
}
