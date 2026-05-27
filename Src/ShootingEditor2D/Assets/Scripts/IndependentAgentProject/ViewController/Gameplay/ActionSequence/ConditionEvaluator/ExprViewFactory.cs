using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class SceneObjExprView
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public string State { get; set; }
    }

    public static class ExprViewFactory
    {
        public static SceneObjExprView From(SceneObjBase sceneObj)
        {
            var view = new SceneObjExprView();

            // Position: Vector3 → Vector2
            Vector3 pos3 = sceneObj.transform.position;
            view.Position = new Vector2(pos3.x, pos3.y);

            // Velocity: 组件来源
            var rb = sceneObj.GetComponent<Rigidbody2D>();
            if (rb != null)
                view.Velocity = rb.velocity;
            else
                view.Velocity = Vector2.zero;

            // State: 统一状态来源
            view.State = sceneObj.StateName;  // 假设你有统一状态机字段

            return view;
        }
    }
}
