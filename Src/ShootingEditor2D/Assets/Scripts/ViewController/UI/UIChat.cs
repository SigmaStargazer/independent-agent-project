using UnityEngine;
using UnityEngine.UI;

namespace ShootingEditor2D
{
    public class UIChat : MonoBehaviour
    {
        public GameObject root;
        public InputField inputField;
        public Text historyText;
        private PlayerController player;

        private void Awake()
        {
            player = FindObjectOfType<PlayerController>();
            root.SetActive(false);
        }

        public void Open()
        {
            root.SetActive(true);
            inputField.text = "";
            inputField.ActivateInputField();
        }

        public void Close()
        {
            root.SetActive(false);
        }

        public void OnSendButtonClicked()
        {
            string text = inputField.text;
            if (string.IsNullOrEmpty(text))
                return;
            AppendMessage("Íæ¼Ò: " + text);
            player.SendChatMessage(text);
            inputField.text = "";
            inputField.ActivateInputField();
        }

        public void AppendMessage(string msg)
        {
            historyText.text += "\n" + msg;
        }
    }
}