using FrameworkDesign;
using Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IndependentAgentProject
{
    public class SaveManager : Singleton<SaveManager>
    {
        private string filePath;
        private SaveManager() 
        { 
            this.filePath = Application.persistentDataPath + "/" + "save.json";
        }

        public void Init(string firstLevelName)
        {
            this.Delete();
            this.Save(firstLevelName);
        }
        public void Save(string levelName)
        {
            var data = new SaveData();
            data.LevelName = levelName;

            var jsonStr = JsonUtility.ToJson(data);// 将数据转为json字符串
            System.IO.File.WriteAllText(this.filePath, jsonStr);
        }

        public SaveData Load()
        {
            if (File.Exists(this.filePath))
            {
                var jsonStr = File.ReadAllText(this.filePath);
                var data = JsonUtility.FromJson<SaveData>(jsonStr);
                return data;
            }
            else
            {
                Debug.Log("No save data");
                return new SaveData();
            }
        }

        public void Delete()
        {
            if (File.Exists(this.filePath))
            {
                File.Delete(this.filePath);
            }
        }
    }

    public class SaveData
    {
        public string LevelName = "Level1";
    }
}