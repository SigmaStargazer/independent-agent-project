using Services;
using UnityEngine;
using UnityEngine.UI;

namespace ShootingEditor2D
{
    public class UIChat : MonoBehaviour
    {
        public GameObject PanalSend;
        public InputField messageInputField;
        public Text historyMessageText;
        public ScrollRect historyScrollRect;
        private HumanPlayer player;
        private AIPlayer agent;

        private void Awake()
        {
            player = FindObjectOfType<HumanPlayer>();
            agent = FindObjectOfType<AIPlayer>();
            PanalSend.SetActive(false);
        }
        void Start()
        {
            //AgentService.Instance.OnGetAgentMessage = this.OnGetAgentMessage;
        }

        void OnEnable()
        {
            AgentService.Instance.OnGetAgentMessage += this.OnGetAgentMessage;
        }

        void Update()
        {
            if (Input.GetButtonDown("ToggleChat"))
            {
                this.ToggleChat();
                return;
            }
        }

        void OnDisable()
        {
            AgentService.Instance.OnGetAgentMessage -= this.OnGetAgentMessage;
        }

        public void ToggleChat()
        {
            if (!PanalSend.activeSelf)
            {
                this.Open();
            }
            else
            {
                this.OnClickSendButton();
            }
        }

        public void Open()
        {
            if (player != null)
                this.player.ToggleChatMode();
            PanalSend.SetActive(true);
            messageInputField.text = "";
            messageInputField.ActivateInputField();
        }

        public void Close()
        {
            PanalSend.SetActive(false);
            if (player != null)
                this.player.ToggleMoveMode();
        }

        public void OnClickSendButton()
        {
            string playerName = "";
            string text = messageInputField.text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (player != null)
                    playerName = player.Name;
                else
                    playerName = "系统管理员";
                string messageText = $"{playerName}: {text}";
                AppendMessage(messageText);
                agent.SendMessageToAgent(messageText);
                messageInputField.text = "";
                messageInputField.ActivateInputField();
            }
            this.Close();
        }
        private void OnGetAgentMessage(string agent, string ai_message)
        {
            this.AppendMessage($"{agent}: {ai_message}");
        }

        private void AppendMessage(string msg)
        {
            bool shouldScroll = IsScrolledToBottom();

            if (!string.IsNullOrEmpty(historyMessageText.text))
                historyMessageText.text += "\n";
            historyMessageText.text += msg;
            // 将历史消息的滚动条滚动到最底部
            if (shouldScroll)
                StartCoroutine(ScrollToBottomNextFrame());
        }

        private System.Collections.IEnumerator ScrollToBottomNextFrame()
        {
            yield return null; // 等待一帧，让 Layout 更新完成
            Canvas.ForceUpdateCanvases();
            historyScrollRect.verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// 判断是否需要滚动
        /// </summary>
        /// <returns></returns>
        private bool IsScrolledToBottom()
        {
            return historyScrollRect.verticalNormalizedPosition <= 0.001f;
        }
    }
}