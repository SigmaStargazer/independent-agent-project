using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DynamicExpresso;

public class Program
{
    public static void Main()
    {
        // 1. 准备测试数据
        var studentList = new List<Student>
        {
            new Student("学生A", 10),
            new Student("学生B", 12),
            new Student("学生C", 12),
            new Student("小明", 14) // 索引 3
        };

        int classId = 1;

        // 2. 输入字符串 (注意单引号和 AND)
        string inputStr = "student_list[3].Name == '小明' AND (student_liist[3].Age - student_liist[2].Age) <=2 AND class_id == 1";

        try
        {
            // 3. 执行评估
            bool result = SafeEvaluate(inputStr, studentList, classId);

            Console.WriteLine($"表达式: {inputStr}");
            Console.WriteLine($"结果: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"执行出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全评估表达式的方法
    /// </summary>
    public static bool SafeEvaluate(string expression, List<Student> students, int classId)
    {
        // 创建解释器
        var interpreter = new Interpreter();

        // --- 1. 预处理 ---
        // 将类 SQL 的逻辑关键字转换为 C# 运算符
        // 注意：使用正则 \b 确保只替换单词，避免替换掉字符串里的内容
        expression = Regex.Replace(expression, @"\bAND\b", "&&");
        expression = Regex.Replace(expression, @"\bOR\b", "||");

        // C# 中字符串使用双引号，如果输入是单引号，这里做转换
        // (DynamicExpresso 其实支持单引号表示字符串，但为了严谨转为双引号)
        expression = expression.Replace('\'', '"');

        // --- 2. 注入上下文 ---
        // 这一步是安全的核心：解释器只能看到这里显式设置的变量
        interpreter.SetVariable("student_list", students);

        // 为了兼容示例中的拼写错误，将 'student_liist' 也映射到真正的列表
        interpreter.SetVariable("student_liist", students);

        interpreter.SetVariable("class_id", classId);

        // --- 3. 解析并执行 ---
        // Eval 将表达式解析为 Lambda 表达式并执行
        // 如果表达式包含不安全的操作（如调用未定义的函数），这里会抛出异常
        return interpreter.Eval<bool>(expression);
    }
}

// 简单的学生类
public class Student
{
    public string Name { get; set; }
    public int Age { get; set; }

    public Student(string name, int age)
    {
        Name = name;
        Age = age;
    }
}
