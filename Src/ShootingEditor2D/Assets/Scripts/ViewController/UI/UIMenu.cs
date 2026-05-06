using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootingEditor2D
{
    public class UIMenu : MonoBehaviour
    {
        public GameObject PanelMenu;
        public string titleSceneName = "Title";
        private string SceneName;
        void Awake()
        {
            this.SceneName = SceneManager.GetActiveScene().name;
            this.PanelMenu.SetActive(false);
        }
        // Start is called before the first frame update
        void Start()
        {
            
        }

        // Update is called once per frame
        void Update()
        {
            if (Input.GetButtonDown("Menu"))
            {
                this.OnClickMenuBtn();
            }

        }

        private void OnClickMenuBtn()
        {
            this.PanelMenu.SetActive(!this.PanelMenu.activeSelf);
        }

        public void OnClickRetry()
        {
            // 1. 读档
            // 2. 直接重新开始关卡
            SceneManager.LoadScene(this.SceneName);
        }

        public void OnClickReturnToTitle()
        {
            // 无需存档，直接回到标题
            SceneManager.LoadScene(this.titleSceneName);
        }

        public void OnClickContinue()
        {
            this.PanelMenu.SetActive(false);
        }

        /// <summary>
        /// (测试)保存存档
        /// </summary>
        public void OnClickSave()
        {
            SaveManager.Instance.Save(this.SceneName);
            Debug.Log($"保存成功");
        }
    }
}

