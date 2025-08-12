using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;

namespace ShootingEditor2D
{
    public class LevelPlayer : MonoBehaviour
    {
        public enum State
        {
            Selection,
            Playing,
        }

        private State mCurState = State.Selection;

        private string mLevelFilesFolder;
        private void Awake()
        {
            mLevelFilesFolder = Application.persistentDataPath + "/LevelFiles";
        }

        private void ParseAndRun(string xml)
        {
            var document = new XmlDocument();
            document.LoadXml(xml);

            // Ñ¡ÔñÆ¥Åä"Level"µÄXmlNode
            var levelNode = document.SelectSingleNode("Level");

            foreach (XmlElement levelItemNode in levelNode.ChildNodes)
            {
                var levelItemName = levelItemNode.Attributes["name"].Value;
                var levelItemX = int.Parse(levelItemNode.Attributes["x"].Value);
                var levelItemY = int.Parse(levelItemNode.Attributes["y"].Value);

                var levelItemPrefab = Resources.Load<GameObject>(levelItemName);
                var levelItemGameObject = Instantiate(levelItemPrefab, transform);
                levelItemGameObject.transform.position = new Vector3(levelItemX, levelItemY, 0);
            }
        }

        private void OnGUI()
        {
            if (mCurState == State.Selection)
            {
                int y = 10;
                foreach(var filePath in Directory.GetFiles(mLevelFilesFolder).Where(f => f.EndsWith("xml")))
                {
                    var fileName = Path.GetFileName(filePath);
                    if(GUI.Button(new Rect(10, y, 150, 40), fileName))
                    {
                        var xml = File.ReadAllText(filePath);
                        ParseAndRun(xml);
                        mCurState = State.Playing;
                    }
                }
                y += 50;
            }
        }
    }
}
