using LineDC.Messaging;
using LineDC.Messaging.Messages;
using LineDC.Messaging.Messages.Actions;
using LineDC.Messaging.Messages.Flex;
using LineDC.Messaging.Webhooks;
using LineDC.Messaging.Webhooks.Events;
using LineDC.Messaging.Webhooks.Messages;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sample.LineBot
{
    public class EnqBotApp : WebhookApplication, IDurableWebhookApplication
    {
        public ILogger Logger { get; set; }
        public IDurableOrchestrationClient DurableClient { get; set; }

        public EnqBotApp(ILineMessagingClient client, string channelSecret)
            : base(client, channelSecret)
        {
        }

        // アンケート（選択式3問＋自由記述1問）
        private List<(string question, string[] quickReply)> enq = new List<(string question, string[] quickReply)>
        {
            ("Azure は好きですか？", new [] { "はい", "Yes" }),
            ("Azure Functions は好きですか？", new [] { "はい", "もちろん", "大好きです" }),
            ("Web Apps は？", new [] { "好きです", "大好きです" }),
            ("Azure で好きなサービスは？", null)
        };

        protected override async Task OnMessageAsync(MessageEvent ev)
        {
            if (ev.Message is TextEventMessage textMessage)
            {
                if (textMessage.Text == "アンケート開始")
                {
                    // 履歴削除
                    await DurableClient.PurgeInstanceHistoryAsync(ev.Source.UserId);

                    // オーケストレーター開始
                    await DurableClient.StartNewAsync(nameof(DurableEnqFunctions.EnqAnswerOrchestrator), ev.Source.UserId);

                    // 最初の質問
                    await ReplyNextQuestionAsync(ev.ReplyToken, 0);
                }
                else
                {
                    // オーケストレーターのステータスを取得
                    var status = await DurableClient.GetStatusAsync(ev.Source.UserId);

                    // カスタムステータスに保存されている回答済み質問インデックスをもとに現在のインデックスを取得
                    int index = int.TryParse(status?.CustomStatus?.ToString(), out var before) ? before + 1 : 0;
                    Logger.LogInformation($"OnMessageAsync - index: {index}");

                    if (enq.Count() == index + 1)
                    {
                        // 回答終了処理
                        // Durable Functionsの外部イベントとして送信メッセージを投げる
                        // 終了の合図「-1」とリプライトークンをセットで送るのがポイント
                        await DurableClient.RaiseEventAsync(ev.Source.UserId, "answer", (-1, textMessage.Text, ev.ReplyToken));
                        return;
                    }

                    // オーケストレーター起動中の場合
                    if (status?.RuntimeStatus == OrchestrationRuntimeStatus.ContinuedAsNew ||
                        status?.RuntimeStatus == OrchestrationRuntimeStatus.Pending ||
                        status?.RuntimeStatus == OrchestrationRuntimeStatus.Running)
                    {
                        // Durable Functionsの外部イベントとしてインデックスと回答内容、リプライトークンをタプルにまとめて投げる
                        await DurableClient.RaiseEventAsync(
                            ev.Source.UserId, "answer", (index, textMessage.Text, ev.ReplyToken));
                    }
                    else
                    {
                        await Client.ReplyMessageAsync(ev.ReplyToken, "「アンケート開始」と送ってね");
                        return;
                    }
                }
            }
            else
            {
                await Client.ReplyMessageAsync(ev.ReplyToken, "「アンケート開始」と送ってね");
            }
        }

        protected override async Task OnPostbackAsync(PostbackEvent ev)
        {
            switch (ev.Postback.Data)
            {
                // 内容確認後の最終送信
                case "send":
                    // 本来はここで DB 保存処理などを行う
                    await Client.ReplyMessageAsync(
                        ev.ReplyToken, "回答ありがとうございました。");
                    // 履歴削除
                    await DurableClient.PurgeInstanceHistoryAsync(ev.Source.UserId);
                    break;

                // キャンセル
                case "cancel":
                    // やり直し
                    await Client.ReplyMessageAsync(
                        ev.ReplyToken, "回答をキャンセルしました。もう一度回答する場合は「アンケート開始」と送ってください。");
                    // 履歴削除
                    await DurableClient.PurgeInstanceHistoryAsync(ev.Source.UserId);
                    break;
            }
        }

        public async Task ReplyNextQuestionAsync(string replyToken, int index)
        {
            // 次の質問
            var next = enq[index];

            await Client.ReplyMessageAsync(replyToken, new List<ISendMessage>
            {
                // クイックリプライがあれば質問とセットで返信
                next.quickReply != null
                    ? new TextMessage(next.question, new QuickReply(next.quickReply.Select(
                        quick => new QuickReplyButtonObject(new MessageTemplateAction(quick, quick))).ToList()))
                    : new TextMessage(next.question)
            });
        }

        public async Task ReplySummaryAsync(string replyToken, List<string> answers)
        {
            // 内容確認を Flex Message で
            await Client.ReplyMessageAsync(replyToken,                
                new List<ISendMessage>
                {
                    FlexMessage.CreateBubbleMessage("確認").SetBubbleContainer(
                        new BubbleContainer()
                            .SetHeader(BoxLayout.Horizontal)
                                .AddHeaderContents(new TextComponent
                                    {
                                        Text = "以下の内容でよろしいですか？",
                                        Align = Align.Center,
                                        Weight = Weight.Bold
                                    })
                            .SetBody(new BoxComponent
                            {
                                Layout = BoxLayout.Vertical,
                                // 質問と回答をまとめて処理（こういうときは Zip メソッドが便利！）
                                // 2つのリストから同じインデックスの項目ごとにペアを作ってくれる
                                Contents = enq.Zip(answers, (enq, answer) => (enq.question, answer)).Select(p => new BoxComponent
                                {                                    
                                    Layout = BoxLayout.Vertical,
                                    Contents = new IFlexComponent[]
                                    {
                                        new TextComponent
                                        {
                                            Text = p.question,
                                            Size = ComponentSize.Xs,
                                            Align = Align.Start,
                                            Weight = Weight.Bold
                                        },
                                        new TextComponent
                                        {
                                            Text = p.answer,
                                            Align = Align.Start
                                        }
                                    }
                                }).ToArray()
                            })
                            .SetFooter(new BoxComponent
                            {
                                Layout = BoxLayout.Horizontal,
                                Contents = new IFlexComponent[]
                                {
                                    new ButtonComponent
                                    {
                                        Action = new PostbackTemplateAction("送信する", "send")
                                    },
                                    new ButtonComponent
                                    {
                                        Action = new PostbackTemplateAction("やり直す", "cancel")
                                    }
                                }
                            }))
                });
        }
    }
}