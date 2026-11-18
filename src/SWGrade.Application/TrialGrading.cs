using System;
using SWGrade.Domain;

namespace SWGrade.Application
{
    public static class TrialGrading
    {
        public static GradeResult Evaluate(Assignment assignment, ModelInspectionResult reference, ModelInspectionResult current)
        {
            if (assignment == null)
                throw new ArgumentNullException(nameof(assignment));
            if (reference?.DocumentHash == null || reference.DocumentHash != assignment.StandardHash)
                throw new InvalidOperationException("当前参考件与所选作业保存的标准件不同。请重新选用该作业的标准文件。");
            var rules = assignment.Rules;
            if (rules.StandardConfiguration != null && rules.StandardConfiguration != reference.Configuration)
                throw new InvalidOperationException($"作业使用参考配置「{rules.StandardConfiguration}」，请用该配置重新分析。");
            if (rules.ExpectedCadYear != null &&
                (reference.SolidWorksVersion != rules.ExpectedCadYear || current?.SolidWorksVersion != rules.ExpectedCadYear))
                throw new InvalidOperationException($"该作业要求 SOLIDWORKS {rules.ExpectedCadYear}。请用该版本重新分析。");
            string expectedConfiguration = rules.StudentConfiguration ?? reference.Configuration;
            if (current?.Configuration != expectedConfiguration)
                throw new InvalidOperationException($"作业匹配学生配置「{expectedConfiguration}」。请选用该配置重新分析，或在批量页面为该学生手动指定配置。");

            return new GradingEngine().Evaluate(rules, reference, current,
                SavedGeometry.CompareIdenticalSnapshot(reference, current));
        }
    }
}
