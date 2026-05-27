using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndependentAgentProject
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
        /// <summary>
        /// (测试)保存存档
        /// </summary>
        public void OnClickSave()
        {
            SaveManager.Instance.Save(this.SceneName);
            Debug.Log($"保存成功");
        }
        /// <summary>
        /// (测试)下一关
        /// </summary>
        /// 
        public void OnClickNextMap()
        {
            string nextLevelName = "Level2";
            GameFlowManager.Instance.NextMap(nextLevelName).Forget(Debug.LogException);
        }
        private void OnClickMenuBtn()
        {
            this.PanelMenu.SetActive(!this.PanelMenu.activeSelf);
        }
        public void OnClickRetry()
        {
            GameFlowManager.Instance.ContinueGame().Forget(Debug.LogException);
        }
        public void OnClickReturnToTitle()
        {
            GameFlowManager.Instance.ReturnToTitle(this.titleSceneName).Forget(Debug.LogException);
        }

        public void OnClickContinue()
        {
            this.PanelMenu.SetActive(false);
        }
    }
}

