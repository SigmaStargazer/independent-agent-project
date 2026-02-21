using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ConditionContext
    {
        // 静态view
        public SceneObjExprView Myself { get; }
        public List<SceneObjExprView> Objects { get; }

        // 动态变量
        public float Displacement { get; set; }
        public float ActionTime { get; set; }

        public ConditionContext(SceneObjBase myself, List<SceneObjBase> objects)
        {
            // 投影到 Expression View
            Myself = ExprViewFactory.From(myself);
            Objects = objects.Select(ExprViewFactory.From).ToList();
            Displacement = 0f;
            ActionTime = 0f;
        }
    }
}