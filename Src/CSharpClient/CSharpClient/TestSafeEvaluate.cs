using System;
using System.Collections.Generic;
using System.Linq.Dynamic.Core; // 核心库
using System.Linq.Expressions;

namespace SafeEvaluatorDemo
{
    // 1. 定义数据模型
    public class Student
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    // 2. 定义上下文环境（作为沙箱的根对象）
    public class EvaluationContext
    {
        public List<Student> student_list { get; set; }
        public int class_id { get; set; }
    }

    public class SafeEvaluator
    {
        public static bool Evaluate(string expression, EvaluationContext context)
        {
            try
            {
                // --- 步骤 A: 语法预处理 ---
                // C# 的标准字符串是用双引号 ""，而用户输入常用单引号 ''
                // C# 逻辑运算通常用 && ||，用户输入可能是 SQL 风格的 AND OR
                // System.Linq.Dynamic.Core 其实支持 "eq", "and" 等关键字，
                // 但为了保险和处理单引号字符串，我们做一下标准化替换。

                string safeExpr = expression
                    .Replace("'", "\"")       // 将 '小明' 转换为 "小明"
                    .Replace(" AND ", " && ") // 将 AND 转换为 &&
                    .Replace(" OR ", " || "); // 将 OR 转换为 ||

                // --- 步骤 B: 创建参数表达式 ---
                // 这告诉解析器：所有的变量名（如 student_list）都是从 context 对象中查找的
                var p = Expression.Parameter(typeof(EvaluationContext), "context");

                // --- 步骤 C: 解析表达式 (核心安全步骤) ---
                // ParseLambda 会检查语法，并生成一个只读的表达式树
                // 如果字符串里包含 System.IO.File.Delete 等恶意代码，这里会直接报错，因为 context 里没有这些方法
                var e = DynamicExpressionParser.ParseLambda(
                    new[] { p },          // 参数定义
                    typeof(bool),         // 预期返回值类型
                    safeExpr              // 表达式字符串
                );

                // --- 步骤 D: 编译并执行 ---
                var compiledFunc = e.Compile();
                var result = compiledFunc.DynamicInvoke(context);

                return (bool)result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析错误: {ex.Message}");
                return false;
            }
        }
    }

    class Program
    {
        static void Mainbing(string[] args)
        {
            // --- 准备数据 ---
            var context = new EvaluationContext
            {
                class_id = 1,
                student_list = new List<Student>
                {
                    new Student { Name = "张三", Age = 18 }, // index 0
                    new Student { Name = "李四", Age = 19 }, // index 1
                    new Student { Name = "王五", Age = 20 }, // index 2
                    new Student { Name = "小明", Age = 22 }  // index 3
                }
            };

            // --- 目标表达式 ---
            // 注意：这里保留了你要求的 SQL 风格 AND 和单引号，代码会自动转换
            string inputExpr = "student_list[3].Name == '小明' AND (student_list[3].Age - student_list[2].Age) <= 2 AND class_id == 1";

            Console.WriteLine($"输入表达式: {inputExpr}");

            // --- 执行判断 ---
            bool isMatch = SafeEvaluator.Evaluate(inputExpr, context);

            Console.WriteLine(new string('-', 30));
            Console.WriteLine($"判断结果: {isMatch}");
        }
    }
}