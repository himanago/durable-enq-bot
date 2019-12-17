using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Sample.LineBot
{
    public class DurableEnqFunctions
    {
        private EnqBotApp App { get; }
        public DurableEnqFunctions(EnqBotApp app)
        {
            App = app;
        }

        [FunctionName(nameof(EnqAnswerOrchestrator))]
        public async Task EnqAnswerOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            // アンケート回答をまとめるリスト（再帰呼び出し時は引数から受け取る）
            var list = context.GetInput<List<string>>() ?? new List<string>();

            // 「answer」という名前のイベントが起きるまで待機
            // 発生後、その結果を value に受け取る
            var value = await context.WaitForExternalEvent<(int index, string message, string replyToken)>("answer");
            log.LogInformation($"EnqAnswerOrchestrator - index: {value.index}");

            // リストに回答を追加
            list.Add(value.message);

            // インデックスが -1 なら終了とみなす
            if (value.index == -1)
            {
                // 完成したリストをリプライトークンとともに確認返信アクティビティに渡す
                await context.CallActivityAsync(nameof(SendSummaryActivity), (value.replyToken, list));
            }
            else
            {
                // 回答インデックスを更新
                context.SetCustomStatus(value.index);

                // 質問返信アクティビティ呼び出し
                // あえてオーケストレーターの最後で遠回しに返信処理を呼ぶのは、
                // インデックス更新前に返信されて同じ質問が返るのを防ぐため。
                // 返信などもアクティビティに任せないとおかしくなる（オーケストレーターは冪等性を維持）
                await context.CallActivityAsync(nameof(SendQuestionActivity), (value.replyToken, value.index + 1));

                // オーケストレーターを再帰的に呼び出す
                context.ContinueAsNew(list);
            }
        }

        [FunctionName(nameof(SendQuestionActivity))]
        public async Task SendQuestionActivity(
            [ActivityTrigger] IDurableActivityContext context)
        {
            var input = context.GetInput<(string replyToken, int index)>();

            await App.ReplyNextQuestionAsync(input.replyToken, input.index);
        }

        [FunctionName(nameof(SendSummaryActivity))]
        public async Task SendSummaryActivity(
            [ActivityTrigger] IDurableActivityContext context)
        {
            var input = context.GetInput<(string replyToken, List<string> answers)>();

            // アンケート回答の確認メッセージを返信
            await App.ReplySummaryAsync(input.replyToken, input.answers);
        }

        [FunctionName(nameof(HttpStart))]
        public async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")]HttpRequestMessage req,
            [DurableClient]IDurableOrchestrationClient starter,
            ILogger log)
        {
            // EnqBotApp にロガーとスターターをわたす
            App.Logger = log;
            App.DurableClient = starter;

            await App.RunAsync(
                req.Headers.GetValues("x-line-signature").First(), await req.Content.ReadAsStringAsync());

            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}