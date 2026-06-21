using UnityEngine;

namespace IndependentAgentProject
{
    public class SceneObjExprView
    {
        public Vector2 Position { get; set; }
        public Vector2 LeftPosition { get; set; }
        public Vector2 RightPosition { get; set; }
        public Vector2 Velocity { get; set; }
        public string State { get; set; }
    }

    public static class ExprViewFactory
    {
        public static SceneObjExprView From(SceneObjBase sceneObj)
        {
            var view = new SceneObjExprView();

            // Position: Vector3 ¡ú Vector2
            Vector3 pos3 = sceneObj.transform.position;
            view.Position = new Vector2(pos3.x, pos3.y);

            if (sceneObj.UseRangeDirection && sceneObj.RangeCollider != null)
            {
                Bounds bounds = sceneObj.RangeCollider.bounds;
                view.LeftPosition = new Vector2(bounds.min.x, bounds.center.y);
                view.RightPosition = new Vector2(bounds.max.x, bounds.center.y);
            }
            else
            {
                view.LeftPosition = view.Position;
                view.RightPosition = view.Position;
            }

            var rb = sceneObj.GetComponent<Rigidbody2D>();
            view.Velocity = rb != null ? rb.velocity : Vector2.zero;
            view.State = sceneObj.StateName;

            return view;
        }
    }
}
