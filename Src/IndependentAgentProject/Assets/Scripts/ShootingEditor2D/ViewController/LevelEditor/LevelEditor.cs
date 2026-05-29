using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace ShootingEditor2D
{
    public class LevelItemInfo
    {
        public float X;
        public float Y;
        public string Name;
    }
    public class LevelEditor : MonoBehaviour
    {
        /// <summary>
        /// 操作模式
        /// </summary>
        public enum OperateMode
        {
            Draw,
            Erase
        }
        public enum BrushType
        {
            Ground,
            Player
        }
        private OperateMode mCurOperateModel = OperateMode.Draw;
        private BrushType mCurBrushType = BrushType.Ground;

        #region 显示配置
        private readonly Lazy<GUIStyle> mModeLabelStyle = new Lazy<GUIStyle>(() => new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            alignment = TextAnchor.MiddleCenter
        });
        private readonly Lazy<GUIStyle> mButtonStyle = new Lazy<GUIStyle>(() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 30,
        });
        private readonly Lazy<GUIStyle> mRightButtonStyle = new Lazy<GUIStyle> (() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 25,
        });
        #endregion

        #region 显示层
        private void OnGUI()
        {
            var modeLabelRect = RectHelper.RectForAnchorCenter(Screen.width * 0.5f, 35, 200, 50);
            // 显示当前模式
            //GUI.Label(modeLabelRect, mCurOperateModel.ToString(), mModeLabelStyle.Value);
            switch (mCurOperateModel)
            {
                case OperateMode.Draw:
                    GUI.Label(modeLabelRect, mCurOperateModel + ":" + mCurBrushType, mModeLabelStyle.Value);
                    break;
                case OperateMode.Erase:
                    GUI.Label(modeLabelRect, mCurOperateModel.ToString(), mModeLabelStyle.Value);
                    break;
                default:
                    break;
            }

            var drawButtonRect = new Rect(10, 10, 150, 40);
            if (GUI.Button(drawButtonRect, "绘制", mButtonStyle.Value))
            {
                mCurOperateModel = OperateMode.Draw;
            }

            var eraseButtonRect = new Rect(10, 60, 150, 40);
            if (GUI.Button(eraseButtonRect, "橡皮", mButtonStyle.Value))
            {
                mCurOperateModel = OperateMode.Erase;
            }

            switch (mCurOperateModel)
            {
                case OperateMode.Draw:
                    var groundButtonRect = new Rect(Screen.width - 110, 10, 100, 40);
                    if(GUI.Button(groundButtonRect, "地块", mRightButtonStyle.Value))
                    {
                        mCurBrushType = BrushType.Ground;
                    }
                    var playerButtonRect = new Rect(Screen.width - 110, 60, 100, 40);
                    if (GUI.Button(playerButtonRect, "主角", mRightButtonStyle.Value))
                    {
                        mCurBrushType = BrushType.Player;
                    }
                    break;
                default:
                    break;
            }
            var saveButtonRect = new Rect(Screen.width - 110, Screen.height - 50, 100, 40);
            if (GUI.Button(saveButtonRect, "保存", mRightButtonStyle.Value))
            {
                var infos = new List<LevelItemInfo>(transform.childCount);
                // 搜集
                foreach(Transform child in transform)
                {
                    infos.Add(new LevelItemInfo()
                    {
                        X = child.position.x,
                        Y = child.position.y,
                        Name = child.name,
                    });
                }

                var document = new XmlDocument();
                // declaration声明
                var declaration = document.CreateXmlDeclaration("1.0", "UTF-8", "");
                document.AppendChild(declaration);

                // 根节点
                var level = document.CreateElement("Level");
                document.AppendChild(level);

                foreach (var levelItemInfo in infos)
                {
                    var levelItem = document.CreateElement("LevelItem");
                    levelItem.SetAttribute("name", levelItemInfo.Name);
                    levelItem.SetAttribute("x", levelItemInfo.X.ToString());
                    levelItem.SetAttribute("y", levelItemInfo.Y.ToString());
                    //levelItem是level的子节点
                    level.AppendChild(levelItem);
                }

                // 输出
                //var stringBuilder = new StringBuilder();
                //var stringWriter = new StringWriter(stringBuilder);
                //var xmlWriter = new XmlTextWriter(stringWriter);
                ////缩进
                //xmlWriter.Formatting = Formatting.Indented;
                //document.WriteTo(xmlWriter);
                //Debug.Log(stringBuilder.ToString());

                //指定目录
                //persistentDataPath 指向设备上的公共目录
                //此目录中可以存储每次运行要保留的数据
                var levelFilesFolder = Application.persistentDataPath + "/LevelFiles";
                Debug.Log(levelFilesFolder);
                if (!Directory.Exists(levelFilesFolder))
                {
                    Directory.CreateDirectory(levelFilesFolder);
                }
                var levelFilePath = levelFilesFolder + "/" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xml";
                // 将xml写入到文件路径
                document.Save(levelFilePath);
            }
        }
        #endregion

        #region 交互逻辑
        public SpriteRenderer EmptyHighlight;

        // 是否可绘制
        private bool mCanDraw;
        // 获取点击的GameObject用于擦除
        private GameObject mCurGameObjectOnMouse;
        private void Update()
        {
            var mousePosition = Input.mousePosition;
            var mouseWorldPos = Camera.main.ScreenToWorldPoint(mousePosition);

            mouseWorldPos.x = Mathf.Floor(mouseWorldPos.x + 0.5f);
            mouseWorldPos.y = Mathf.Floor(mouseWorldPos.y + 0.5f);

            mouseWorldPos.z = 0;

            //当某个控件处于热状态时，不允许其他控件响应鼠标事件
            EmptyHighlight.gameObject.SetActive(GUIUtility.hotControl == 0);

            // 与当前高亮块的 x y 值一样
            if (EmptyHighlight.transform.position.x == mouseWorldPos.x 
                && EmptyHighlight.transform.position.y == mouseWorldPos.y)
            {
                // 不做任何事情
            }
            else
            {
                // 设置高亮块的位置
                var highlightPos = mouseWorldPos;
                highlightPos.z = -9;

                EmptyHighlight.transform.position = highlightPos;

                // 发出射线
                Ray ray = Camera.main.ScreenPointToRay(mousePosition);
                var hit = Physics2D.Raycast(ray.origin, Vector2.zero, 20);
                // 有碰撞说明是有地块
                if (hit.collider)
                {
                    switch (mCurOperateModel)
                    {
                        case OperateMode.Draw:
                            EmptyHighlight.color = new Color(1, 0, 0, 0.5f); // 红色代表不能绘制
                            break;
                        case OperateMode.Erase:
                            EmptyHighlight.color = new Color(1, 0.5f, 0, 0.5f); // 橙色代表可擦除
                            break;
                        default:
                            break;
                    }
                    mCanDraw = false;
                    mCurGameObjectOnMouse = hit.collider.gameObject;
                }
                else
                {
                    switch (mCurOperateModel)
                    {
                        case OperateMode.Draw:
                            EmptyHighlight.color = new Color(1, 1, 1, 0.5f); // 白色代表可以绘制
                            break;
                        case OperateMode.Erase:
                            EmptyHighlight.color = new Color(0, 0, 1, 0.5f); // 蓝色代表橡皮状态
                            break;
                        default:
                            break;
                    }
                    mCanDraw = true;
                    mCurGameObjectOnMouse = null;
                }
            }

            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButton(0)) && GUIUtility.hotControl == 0)
            {
                switch (mCurOperateModel)
                {
                    case OperateMode.Draw:
                        if (mCanDraw)
                        {
                            switch (mCurBrushType)
                            {
                                case BrushType.Ground:
                                    var groundPrefab = Resources.Load<GameObject>("Ground");
                                    var groundGameObj = Instantiate(groundPrefab, transform);
                                    groundGameObj.transform.position = mouseWorldPos;
                                    groundGameObj.name = "Ground";
                                    break;
                                case BrushType.Player:
                                    var playerPrefab = Resources.Load<GameObject>("Player");
                                    var playerGameObj = Instantiate(playerPrefab, transform);
                                    playerGameObj.transform.position = mouseWorldPos;
                                    playerGameObj.name = "Player";
                                    break;
                                default:
                                    break;
                            }
                            // 已绘制过了就不要再绘制了
                            mCanDraw = false;
                        }
                        break;
                    case OperateMode.Erase:
                        if (mCurGameObjectOnMouse)
                        {
                            Destroy(mCurGameObjectOnMouse);
                            mCurGameObjectOnMouse = null;
                        }
                        break;
                    default:
                        break;
                }

            }
        }
        #endregion
    }
}