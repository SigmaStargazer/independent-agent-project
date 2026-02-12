using SkillBridge.Message;

namespace ShootingEditor2D
{
    public static class ActionExecutor
    {
        public static ActionContext Build(ActionStep step, SceneObjSnapshot snap, Agent agent)
        {
            // ===== Wait =====
            if (step.Wait != null)   // ✅ protobuf-net oneof 判断方式
            {
                var compiler = new ConditionCompiler(snap, agent);
                var cond = compiler.Compile(step.Condition);

                return new ActionContext
                {
                    ActionName = "Wait",
                    EndCondition = cond,
                    Result = new ActionResult()
                };
            }

            // ===== Move =====
            if (step.Move != null)   // ✅ protobuf-net oneof 判断方式
            {
                //bool right = step.Move.direction == MoveAction.Direction.Right;

                //// MVP：先用固定距离，后续可接 condition 控制
                //float distance = 3f;
                var compiler = new ConditionCompiler(snap, agent);
                var cond = compiler.Compile(step.Condition);

                return new ActionContext
                {
                    ActionName = "Move",
                    EndCondition = cond,
                    //EndCondition = null,   // 由 Agent.Move 内部控制
                    Result = new ActionResult()
                };
            }

            throw new System.Exception("Unknown ActionStep (oneof empty)");
        }
    }
}
