using System.IO;
using System.Text.Json;
using GameHelper;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class CraftPipelineModelsTests
{
    // ── Полный round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TimeLostSapphirePipeline_AllFieldsPreserved()
    {
        var pipeline = BuildTimeLostSapphirePipeline();

        var json = JsonSerializer.Serialize(pipeline, SettingsStore.JsonOptions);
        var restored = JsonSerializer.Deserialize<CraftPipeline>(json, SettingsStore.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal("Time-Lost Sapphire Crit Divine", restored!.Name);
        Assert.Equal("Jewels", restored.ItemClass);
        Assert.Equal(5, restored.Steps.Count);
    }

    [Fact]
    public void RoundTrip_Step1_CheckItemWithEntryCondition()
    {
        var restored = RoundTrip(BuildTimeLostSapphirePipeline());

        var step = restored.Steps[0];
        Assert.Equal(PipelineAction.CheckItem, step.Action);
        Assert.Equal("Проверка крита", step.Name);
        Assert.NotNull(step.EntryCondition);
        Assert.Equal("Jewels", step.EntryCondition!.ExpectedItemClass);
        Assert.Equal(TransitionTarget.Next, step.OnSuccess.Target);
        Assert.Equal(TransitionTarget.Abort, step.OnFailure.Target);
        Assert.Equal("предмет не подходит", step.OnFailure.Message);
    }

    [Fact]
    public void RoundTrip_Step2_DivineCraftWithLoopUntilAndMaxIterations()
    {
        var restored = RoundTrip(BuildTimeLostSapphirePipeline());

        var step = restored.Steps[1];
        Assert.Equal(PipelineAction.DivineCraft, step.Action);
        Assert.NotNull(step.LoopUntil);
        Assert.Equal(30, step.MaxIterations);
        Assert.Equal(TransitionTarget.Abort, step.OnFailure.Target);
        Assert.Equal("не задивайнили за 30 попыток", step.OnFailure.Message);
    }

    [Fact]
    public void RoundTrip_Step3_OmenActivationConfig()
    {
        var restored = RoundTrip(BuildTimeLostSapphirePipeline());

        var step = restored.Steps[2];
        Assert.Equal(PipelineAction.OmenActivation, step.Action);
        Assert.NotNull(step.OmenConfig);
        Assert.Equal("Omen of Dextral Exaltation", step.OmenConfig!.OmenName);
        Assert.Equal(5, step.OmenConfig.StashCellIndex);
        Assert.Equal(2, step.OmenConfig.InventoryRow);
        Assert.Equal(3, step.OmenConfig.InventoryCol);
    }

    [Fact]
    public void RoundTrip_Step5_BackwardStepTransition()
    {
        var restored = RoundTrip(BuildTimeLostSapphirePipeline());

        // Шаг 5 (index 4) при успехе возвращается к шагу 4 (index 3)
        var step = restored.Steps[4];
        Assert.Equal(PipelineAction.AugAnnulCraft, step.Action);
        Assert.Equal(TransitionTarget.Step, step.OnSuccess.Target);
        Assert.Equal(3, step.OnSuccess.StepIndex);
    }

    // ── Сериализация enum ─────────────────────────────────────────────────────

    [Fact]
    public void Serialize_PipelineAction_IsCamelCaseString()
    {
        var pipeline = new CraftPipeline
        {
            Steps = { new CraftPipelineStep { Action = PipelineAction.OmenActivation } }
        };

        var json = JsonSerializer.Serialize(pipeline, SettingsStore.JsonOptions);

        Assert.Contains("\"omenActivation\"", json);
    }

    [Fact]
    public void Serialize_TransitionTarget_IsCamelCaseString()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            {
                new CraftPipelineStep
                {
                    OnSuccess = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 3 },
                    OnFailure = new PipelineTransition { Target = TransitionTarget.Done },
                }
            }
        };

        var json = JsonSerializer.Serialize(pipeline, SettingsStore.JsonOptions);

        Assert.Contains("\"step\"", json);
        Assert.Contains("\"done\"", json);
    }

    // ── Null-поля ─────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_NullOptionalFields_StayNull()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            {
                new CraftPipelineStep
                {
                    Action = PipelineAction.CheckItem,
                    EntryCondition = null,
                    LoopUntil = null,
                    OmenConfig = null,
                }
            }
        };

        var restored = RoundTrip(pipeline);

        Assert.Null(restored.Steps[0].EntryCondition);
        Assert.Null(restored.Steps[0].LoopUntil);
        Assert.Null(restored.Steps[0].OmenConfig);
    }

    // ── DefaultMaxIterations ──────────────────────────────────────────────────

    [Fact]
    public void DefaultMaxIterations_Is100()
    {
        var step = new CraftPipelineStep();
        Assert.Equal(100, step.MaxIterations);
    }

    // ── PipelineStore ─────────────────────────────────────────────────────────

    [Fact]
    public void PipelineStore_SaveAndLoad_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gh_pipeline_test_" + Guid.NewGuid());
        try
        {
            var pipeline = BuildTimeLostSapphirePipeline();
            PipelineStore.SaveToDirectory(pipeline, dir);
            var loaded = PipelineStore.LoadFromDirectory(pipeline.Name, dir);

            Assert.NotNull(loaded);
            Assert.Equal(pipeline.Name, loaded!.Name);
            Assert.Equal(pipeline.Steps.Count, loaded.Steps.Count);
            Assert.Equal(TransitionTarget.Step, loaded.Steps[4].OnSuccess.Target);
            Assert.Equal(3, loaded.Steps[4].OnSuccess.StepIndex);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PipelineStore_LoadAll_ReturnsAllSavedPipelines()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gh_pipeline_test_" + Guid.NewGuid());
        try
        {
            var p1 = new CraftPipeline { Name = "Alpha" };
            var p2 = new CraftPipeline { Name = "Beta" };
            PipelineStore.SaveToDirectory(p1, dir);
            PipelineStore.SaveToDirectory(p2, dir);

            var all = PipelineStore.LoadAllFromDirectory(dir);

            Assert.Equal(2, all.Count);
            Assert.Contains(all, p => p.Name == "Alpha");
            Assert.Contains(all, p => p.Name == "Beta");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PipelineStore_LoadNonexistent_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gh_pipeline_test_" + Guid.NewGuid());
        var result = PipelineStore.LoadFromDirectory("NoSuchPipeline", dir);
        Assert.Null(result);
    }

    [Fact]
    public void PipelineStore_LoadAll_EmptyDir_ReturnsEmptyList()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gh_pipeline_test_" + Guid.NewGuid());
        var result = PipelineStore.LoadAllFromDirectory(dir);
        Assert.Empty(result);
    }

    // ── Вспомогательные методы ────────────────────────────────────────────────

    private static CraftPipeline RoundTrip(CraftPipeline pipeline)
    {
        var json = JsonSerializer.Serialize(pipeline, SettingsStore.JsonOptions);
        return JsonSerializer.Deserialize<CraftPipeline>(json, SettingsStore.JsonOptions)!;
    }

    private static CraftPipeline BuildTimeLostSapphirePipeline() => new()
    {
        Name = "Time-Lost Sapphire Crit Divine",
        Description = "Check crit → divine to threshold → omen → exalt",
        ItemClass = "Jewels",
        Steps =
        {
            new CraftPipelineStep
            {
                Name = "Проверка крита",
                Action = PipelineAction.CheckItem,
                EntryCondition = new CraftConditionPlan
                {
                    ExpectedItemClass = "Jewels",
                    OrAlternatives =
                    {
                        new CraftAndGroup
                        {
                            Clauses =
                            {
                                new CraftClause
                                {
                                    Kind = CraftClauseKind.Single,
                                    Single = new CraftSingleAffixData { AffixName = "Piercing" },
                                }
                            }
                        }
                    }
                },
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Next },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Abort, Message = "предмет не подходит" },
            },
            new CraftPipelineStep
            {
                Name = "Divine до нужного значения",
                Action = PipelineAction.DivineCraft,
                LoopUntil = new CraftConditionPlan { ExpectedItemClass = "Jewels" },
                MaxIterations = 30,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Next },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Abort, Message = "не задивайнили за 30 попыток" },
            },
            new CraftPipelineStep
            {
                Name = "Активация омена",
                Action = PipelineAction.OmenActivation,
                OmenConfig = new OmenActionConfig
                {
                    OmenName = "Omen of Dextral Exaltation",
                    StashCellIndex = 5,
                    InventoryRow = 2,
                    InventoryCol = 3,
                },
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Next },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Abort, Message = "омен не найден в стэше" },
            },
            new CraftPipelineStep
            {
                Name = "Exalt",
                Action = PipelineAction.ExaltCraft,
                MaxIterations = 1,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Done },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Next },
            },
            new CraftPipelineStep
            {
                Name = "Annul и вернуться к Exalt",
                Action = PipelineAction.AugAnnulCraft,
                MaxIterations = 1,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 3 },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Abort },
            },
        }
    };
}
