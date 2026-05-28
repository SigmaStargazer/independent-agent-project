using Cysharp.Threading.Tasks;
using FrameworkDesign;
using IndependentAgentProject;
using Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndependentAgentProject
{
    public class UI : MonoBehaviour, IController
    {
        public string titleSceneName = "Title";
        private string SceneName;

        public GameObject PanelMenu;
        public GameObject PanelGameOver;
        private void Awake()
        {
            this.SceneName = SceneManager.GetActiveScene().name;
            this.PanelMenu.SetActive(false);
            this.PanelGameOver.SetActive(false);
        }
        private void Start()
        {
            this.RegisterEvent<GameOverEvent>(e =>
            {
                PanelGameOver.SetActive(true);
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
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

        }

        public void OnClickConfirmReturnToTitle()
        {
            GameFlowManager.Instance.ReturnToTitle(this.titleSceneName).Forget(Debug.LogException);
        }

        public void OnClickContinue()
        {
            this.PanelMenu.SetActive(false);
        }

        public IArchitecture GetArchitecture()
        {
            return IndependentAgentProject.Instance;
        }
    }
}
