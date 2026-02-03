from agent_framwork.tools.action_sequence_model.model.action_sequence import ActionSequence

if __name__ == "__main__":
    # 正例
    action_list = [
        {
            "action": "wait",
            "condition": "objects[0].State == 'GreenLight'"
        },
        {
            "action": "move",
            "direction": "left",
            "condition": "myself.State >= 10"
        },
        {
            "action": "wait",
            "condition": "actionTime >= 5"
        }
    ]

    # # 错例
    # action_list = [
    #     {
    #       "action": "wait",
    #       "condition": "objects[0].State == 'GreenLight'"
    #     },
    #     {
    #       "action": "move",
    #       "direction": "left",
    #       "condition": "myself.State >= 10"
    #     },
    #     {
    #       "action": "wait",
    #       "condition": "ActionTime >= 5"
    #     }
    #   ]


    action_sequence = ActionSequence(action_sequence=action_list)
    print(action_sequence)