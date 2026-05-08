CALL CREATE_FTS_INDEX('Episodic', 'episode_content', ['content', 'source', 'source_description'], stemmer := 'english', stopWords := 'default');
CALL CREATE_FTS_INDEX('Entity', 'node_name_and_summary', ['name', 'summary'], stemmer := 'english', stopWords := 'default');
CALL CREATE_FTS_INDEX('Community', 'community_name', ['name'], stemmer := 'english', stopWords := 'default');
CALL CREATE_FTS_INDEX('RelatesToNode_', 'edge_name_and_fact', ['name', 'fact'], stemmer := 'english', stopWords := 'default');
