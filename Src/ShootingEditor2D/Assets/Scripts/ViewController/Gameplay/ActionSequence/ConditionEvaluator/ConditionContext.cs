using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ConditionContext
    {
        // 源对象（真实世界）
        public SceneObjBase MyselfSrc { get; }
        public List<SceneObjBase> ObjectsSrc { get; }


        // 表达式视图
        public SceneObjExprView Myself { get; private set; }
        public List<SceneObjExprView> Objects { get; private set; }
        public int NearestInteractableIndex { get; private set; }

        // 动态变量
        public float Displacement { get; set; }
        public float ActionTime { get; set; }
        public bool CanInteract { get; set; }

        public ConditionContext(SceneObjBase myself, List<SceneObjBase> objects)
        {
            //// 投影到 Expression View
            //Myself = ExprViewFactory.From(myself);
            //Objects = objects.Select(ExprViewFactory.From).ToList();
            MyselfSrc = myself;
            ObjectsSrc = objects;
            RefreshViews();

            Displacement = 0f;
            ActionTime = 0f;
        }

        /// <summary>
        /// 刷新表达式视图
        /// </summary>
        public void RefreshViews()
        {
            Myself = ExprViewFactory.From(MyselfSrc);
            Objects = ObjectsSrc.Select(ExprViewFactory.From).ToList();

            // 刷新交互状态
            var nearest = SceneObjManager.Instance?.GetNearestInteractableObj(MyselfSrc.gameObject);
            CanInteract = nearest != null;
            NearestInteractableIndex = nearest != null ? ObjectsSrc.IndexOf(nearest) : -1;
        }
    }
}