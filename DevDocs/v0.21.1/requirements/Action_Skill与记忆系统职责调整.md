# 需求一

在v0.21.0中，曾经提出“不再在MemoryManager内部进行ActionSkillManager初始化、生命周期维护”的解耦需求。

经过重新评估，这个需求考虑欠妥：

1. 从实现上说，在MemoryManager的**restore_memory**、**delete_current_memory**结尾，依然进行了ActionSkillManager的初始化。而这块无论如何也无法像MemoryManager初始化时一样把ActionSkillManager的初始化解耦出去。
2. 从逻辑上说，ActionSkillManager应该作为记忆系统的子系统。因为记忆包含了语义记忆、情景记忆、程序性记忆等内容，而ActionSkillManager其实从属于程序性记忆。



综上，我现在想实现：

1. 不再在main.py里单独进行ActionSkillManager().initialize()、DBConnectionService().initialize()、EmbedderService().initialize()，重新放回到MemoryManager().initialize()里执行，把MemoryManager作为记忆系统的总入口
2. 项目结构：把action_skill_system、db_conn、embedder两个文件夹放到memory_system中。action_skill_system为memory_system的子系统，而db_conn、embedder是memory_system的基础设施

# 需求二

目前agent_interuptible.py的<动作技能记忆>，rag出的是top10匹配的技能名及其描述，和claude skill的渐进式披露时输出的内容相同

但是，渐进式披露其实和rag的作用是有违背的地方：

1. 渐进式披露主要是给出类似于技能目录的结构，agent知道有哪些技能后再去查阅详细的技能。缺点是一定需要进行一轮查阅，才能够知道技能怎么使用
2. 而rag注重的是快反。凭借对query的快速匹配，在刚接收到输入时，就能匹配到可能有用的信息，一旦有用就能跳过搜索和查阅，直接作出反应

对于当前的agent项目及action skill系统来说，rag的内容应该改成如下：

top5的action_sequence模板，并附上从属的技能及其简介。

这样就能实现大概率在接到输入时就能直接获取到可能有用的action_sequence模板及其使用方法。