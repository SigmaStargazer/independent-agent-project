using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using DynamicExpresso;

namespace ShootingEditor2D
{
    /// <summary>
    /// Condition校验系统
    /// </summary>
    public class ConditionCompiler
    {
        //private Interpreter interpreter;

        public ConditionCompiler(SceneObjSnapshot snapshot, SceneObjBase self)
        {
            //interpreter = new Interpreter();

            //interpreter.SetVariable("myself", self);
            //interpreter.SetVariable("actionTime", 0f);
            //interpreter.SetVariable("displacement", Vector2.zero);

            //// objects[]
            //interpreter.SetVariable("objects", snapshot.SceneObjs.ToArray());
        }

        public Func<bool> Compile(string expr)
        {
            //var lambda = interpreter.ParseAsDelegate<Func<bool>>(expr);
            //return lambda;
            return () => true;
        }
    }
}