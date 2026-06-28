# 需求一

v0.21.3的Idle Wakeup有个问题：目前配置的唤醒时间是120~300秒，但是我想实现把每次空闲后的首次唤醒时间改成可配置的，并缩短置30秒，如果后面还是没有任何新外界信息传入（可以把_asend_message未被调用视为没有新外界信息传入），才按正常唤醒时间进行唤醒

# 需求二

参照LaserGrid.cs，做一个LaserGridAuto.cs。要求不继承ITriggerable来被其他装置调用来切换是否激活，而是通过FSM定时激活。定时逻辑类似MovingPlatformAuto中的OnIdleUpdate，而不是用协程

# 需求三

帮我设计一个EnemyBase : CharaBase。要求：

 1）正常情况下，沿着路径点巡逻（参考MovingPlatformAuto） 

2）可持有一个视野框。当视野框内出现PlayerBase时，会进入追人状态 

3）当PlayerBase脱离视野框时，先进入Idle，然后向最近的路径点移动

 4）可持有一个攻击判定框。当攻击判定框撞击到PlayerBase时，PlayerBase死亡（参考Laser里用player.Die()）

 另外我的设计里没有用Patrol状态，主要是因为之前设计了通过观察StateChange来发现物体运动规律的机制。原本是Idle->Move->Idle这样去观察，但我担心用Patrol状态后就观察不了了。



然后我PlayerBase增加一个隐藏状态，再增加一个柜子的Device，和柜子进行交互后能躲进柜子内并进入隐藏状态。隐藏状态和死亡状态都不会触发EnemyBase的追人（这块看是不是要增加一个接口，继承这个接口的FSMState不会触发EnemyBase的追人）