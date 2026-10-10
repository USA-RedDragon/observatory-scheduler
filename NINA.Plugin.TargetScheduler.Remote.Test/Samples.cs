namespace NINA.Plugin.TargetScheduler.Remote.Test {

    public static class Samples {
        public const string GoalEdit = """{"project_id":5,"project_name":"Cygnis Loop","goals":[{"target_id":12,"target_guid":"g12","target_name":"Cygnis Loop Panel 1","filter":"H-a","before":null,"after":{"kind":0,"snr_goal":10,"plateau_stop":true}},{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","filter":"H-a","before":{"kind":0,"snr_goal":10,"plateau_stop":true},"after":{"kind":0,"snr_goal":12,"plateau_stop":true,"region":"[{\"x\":0.1,\"y\":0.1},{\"x\":0.5,\"y\":0.1},{\"x\":0.3,\"y\":0.6}]"}}]}""";
        public const string RuleWeightEdit = """{"project_id":5,"project_name":"Cygnis Loop","changes":[{"rule":"Novelty","before":null,"after":10},{"rule":"Target Switch Penalty","before":67,"after":40}]}""";
        public const string TemplateEdit = """{"id":3,"name":"H-a","changes":[{"field":"moonavoidanceseparation","before":45,"after":60}]}""";
        public const string TemplateBatchEdit = """{"items":[{"id":3,"name":"H-a","changes":[{"field":"moonavoidanceseparation","before":45,"after":60}]},{"id":4,"name":"O-III","changes":[{"field":"moonavoidanceseparation","before":45,"after":60}]}]}""";
        public const string ProjectBatchEdit = """{"items":[{"id":5,"name":"Cygnis Loop","changes":[{"field":"priority","before":0,"after":2}]},{"id":7,"name":"California","changes":[{"field":"priority","before":0,"after":2}]}]}""";
        public const string ExposurePlanBatchEdit = """{"items":[{"id":40,"name":"H-a","parent":"Cygnis Loop Panel 2","changes":[{"field":"desired","before":300,"after":400}]},{"id":41,"name":"O-III","parent":"Cygnis Loop Panel 2","changes":[{"field":"enabled","before":true,"after":false}]}]}""";
        public const string TemplateClone = """{"source_id":3,"source_name":"H-a","guid":"t-new","name":"H-a 300","defaultexposure":300,"gain":100}""";
        public const string TemplateDelete = """{"guid":"t-new","name":"H-a 300"}""";
        public const string ApplySet = """{"set_id":"hoo","set_name":"HOO","mode":"replace","targets":[{"target_id":29,"target_guid":"g29","target_name":"Triangulum","project":"Triangulum","create":[{"guid":"p1","template_id":3,"template_name":"H-a","exposure":600,"desired":300}],"disable":[{"id":90,"guid":"p90","template_name":"Luminance"}]}]}""";
        public const string ProjectCreate = """{"project":{"guid":"pg","name":"Gecko Nebula","priority":1,"state":1,"minimumtime":60,"minimumaltitude":15},"targets":[{"guid":"tg","name":"Gecko Nebula","ra_hours":22.53,"dec":40.83,"rotation":0,"plans":[{"guid":"pp","template_id":3,"template_name":"H-a","exposure":600,"desired":300}]}],"goals":[{"target_guid":"tg","filter":"H-a","setting":{"kind":0,"snr_goal":10,"plateau_stop":true}}],"catalog":"LBN 437"}""";
    }
}
