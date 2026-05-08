COPY `Episodic` (`uuid`,`name`,`group_id`,`created_at`,`source`,`source_description`,`content`,`valid_at`,`entity_edges`) FROM "Episodic.csv" (parallel=true, header=true);
COPY `Entity` (`uuid`,`name`,`group_id`,`labels`,`created_at`,`name_embedding`,`summary`,`attributes`) FROM "Entity.csv" (parallel=true, header=true);
COPY `Community` (`uuid`,`name`,`group_id`,`created_at`,`name_embedding`,`summary`) FROM "Community.csv" (parallel=true, header=true);
COPY `RelatesToNode_` (`uuid`,`group_id`,`created_at`,`name`,`fact`,`fact_embedding`,`episodes`,`expired_at`,`valid_at`,`invalid_at`,`attributes`) FROM "RelatesToNode_.csv" (parallel=true, header=true);
COPY `RELATES_TO` FROM "RELATES_TO_Entity_RelatesToNode_.csv" (parallel=true, header=true, from='Entity', to='RelatesToNode_');
COPY `RELATES_TO` FROM "RELATES_TO_RelatesToNode__Entity.csv" (parallel=true, header=true, from='RelatesToNode_', to='Entity');
COPY `HAS_MEMBER` (`uuid`,`group_id`,`created_at`) FROM "HAS_MEMBER_Community_Entity.csv" (parallel=true, header=true, from='Community', to='Entity');
COPY `HAS_MEMBER` (`uuid`,`group_id`,`created_at`) FROM "HAS_MEMBER_Community_Community.csv" (parallel=true, header=true, from='Community', to='Community');
COPY `MENTIONS` (`uuid`,`group_id`,`created_at`) FROM "MENTIONS_Episodic_Entity.csv" (parallel=true, header=true, from='Episodic', to='Entity');
