using HeartLog.DAL.Models;

namespace HeartLog.DAL.Seeding;

public static class EmotionSeedData
{
    public static IReadOnlyList<EmotionSeedItem> GetAll()
    {
        var items = new List<EmotionSeedItem>();

        for (var i = 0; i < RootNodes.Count; i++)
        {
            AddNode(items, RootNodes[i], depth: 0, sortOrder: i + 1, parentKey: null);
        }

        return items;
    }

    private static void AddNode(
        List<EmotionSeedItem> items,
        EmotionSeedNode node,
        int depth,
        int sortOrder,
        string? parentKey)
    {
        items.Add(new EmotionSeedItem(
            node.Key,
            node.Label,
            GetLevel(depth),
            node.Color,
            sortOrder,
            parentKey));

        if (node.Children is null)
        {
            return;
        }

        for (var i = 0; i < node.Children.Count; i++)
        {
            AddNode(items, node.Children[i], depth + 1, i + 1, node.Key);
        }
    }

    private static EmotionLevel GetLevel(int depth) => depth switch
    {
        0 => EmotionLevel.Core,
        1 => EmotionLevel.Secondary,
        2 => EmotionLevel.Tertiary,
        _ => throw new InvalidOperationException($"Unsupported emotion depth: {depth}.")
    };

    private static readonly IReadOnlyList<EmotionSeedNode> RootNodes =
    [
        new(
            "fear",
            "Fear",
            "#9B8AC9",
            [
                new("fear.uneasy", "Uneasy", Children:
                [
                    new("fear.uneasy.restless", "Restless"),
                    new("fear.uneasy.apprehensive", "Apprehensive")
                ]),
                new("fear.anxious", "Anxious", Children:
                [
                    new("fear.anxious.worried", "Worried"),
                    new("fear.anxious.stressed", "Stressed")
                ]),
                new("fear.overloaded", "Overloaded", Children:
                [
                    new("fear.overloaded.overwhelmed", "Overwhelmed"),
                    new("fear.overloaded.pressured", "Pressured")
                ]),
                new("fear.insecure", "Insecure", Children:
                [
                    new("fear.insecure.inadequate", "Inadequate"),
                    new("fear.insecure.inferior", "Inferior")
                ]),
                new("fear.frightened", "Frightened", Children:
                [
                    new("fear.frightened.scared", "Scared"),
                    new("fear.frightened.alarmed", "Alarmed")
                ]),
                new("fear.terrified", "Terrified", Children:
                [
                    new("fear.terrified.panicked", "Panicked"),
                    new("fear.terrified.horrified", "Horrified")
                ]),
                new ("fear.trapped", "Trapped", Children: [
                    new ("fear.trapped.stuck", "Stuck"),
                    new ("fear.trapped.cornered", "Cornered")
                ]),
                new ("fear.threatened", "Threatened", Children: [
                    new ("fear.threatened.vulnerable", "Vulnerable"),
                    new ("fear.threatened.at-risk", "At risk")
                ]),
            ]),
        new(
            "anger",
            "Anger",
            "#EB5757",
            [
                new("anger.irritated", "Irritated", Children:
                [
                    new("anger.irritated.annoyed", "Annoyed"),
                    new("anger.irritated.bored", "Bored")
                ]),
                new("anger.resentful", "Resentful", Children:
                [
                    new("anger.resentful.bitter", "Bitter"),
                    new("anger.resentful.offended", "Offended")
                ]),
                new("anger.furious", "Furious", Children:
                [
                    new("anger.furious.enraged", "Enraged"),
                    new("anger.furious.livid", "Livid")
                ]),
                new("anger.defensive", "Defensive", Children:
                [
                    new("anger.defensive.guarded", "Guarded"),
                    new("anger.defensive.attacked", "Attacked")
                ]),
                new("anger.comparing", "Comparing", Children:
                [
                    new("anger.comparing.envious", "Envious"),
                    new("anger.comparing.jealous", "Jealous")
                ]),
                new("anger.wronged", "Wronged", Children:
                [
                    new("anger.wronged.insulted", "Insulted"),
                    new("anger.wronged.betrayed", "Betrayed")
                ])
            ]),
        new(
            "love",
            "Love",
            "#F28FB3",
            [
                new("love.affectionate", "Affectionate", Children:
                [
                    new("love.affectionate.loving", "Loving"),
                    new("love.affectionate.warm", "Warm")
                ]),
                new("love.caring", "Caring", Children:
                [
                    new("love.caring.compassionate", "Compassionate"),
                    new("love.caring.supportive", "Supportive")
                ]),
                new("love.tender", "Tender", Children:
                [
                    new("love.tender.gentle", "Gentle"),
                    new("love.tender.softhearted", "Softhearted")
                ]),
                new("love.attracted", "Attracted", Children:
                [
                    new("love.attracted.interested", "Interested"),
                    new("love.attracted.longing", "Longing")
                ]),
                new("love.connected", "Connected", Children:
                [
                    new("love.connected.accepted", "Accepted"),
                    new("love.connected.trusting", "Trusting")
                ])
            ]),
        new(
            "calm",
            "Calm",
            "#F2994A",
            [
                new("calm.relaxed", "Relaxed", Children:
                [
                    new("calm.relaxed.at-ease", "At ease"),
                    new("calm.relaxed.loose", "Loose")
                ]),
                new("calm.safe", "Safe", Children:
                [
                    new("calm.safe.secure", "Secure"),
                    new("calm.safe.protected", "Protected")
                ]),
                new("calm.relieved", "Relieved", Children:
                [
                    new("calm.relieved.reassured", "Reassured"),
                    new("calm.relieved.comforted", "Comforted")
                ]),
                new("calm.peaceful", "Peaceful", Children:
                [
                    new("calm.peaceful.tranquil", "Tranquil"),
                    new("calm.peaceful.serene", "Serene")
                ]),
                new("calm.grateful", "Grateful", Children:
                [
                    new("calm.grateful.thankful", "Thankful"),
                    new("calm.grateful.blessed", "Blessed")
                ]),
            ]),
        new(
            "joy",
            "Joy",
            "#F2C94C",
            [
                new("joy.cheerful", "Cheerful", Children:
                [
                    new("joy.cheerful.happy", "Happy"),
                    new("joy.cheerful.delighted", "Delighted")
                ]),
                new("joy.playful", "Playful", Children:
                [
                    new("joy.playful.amused", "Amused"),
                    new("joy.playful.lighthearted", "Lighthearted")
                ]),
                new("joy.proud", "Proud", Children:
                [
                    new("joy.proud.capable", "Capable"),
                    new("joy.proud.accomplished", "Accomplished")
                ]),
                new("joy.fulfilled", "Fulfilled", Children:
                [
                    new("joy.fulfilled.satisfied", "Satisfied"),
                    new("joy.fulfilled.content", "Content")
                ]),
                new("joy.hopeful", "Hopeful", Children:
                [
                    new("joy.hopeful.encouraged", "Encouraged"),
                    new("joy.hopeful.optimistic", "Optimistic")
                ]),
                new("joy.excited", "Excited", Children:
                [
                    new("joy.excited.eager", "Eager"),
                    new("joy.excited.thrilled", "Thrilled")
                ])
            ]),
        new(
            "disgust",
            "Disgust",
            "#A3B83C",
            [
                new("disgust.repulsed", "Repulsed", Children:
                [
                    new("disgust.repulsed.disgusted", "Disgusted"),
                    new("disgust.repulsed.revolted", "Revolted")
                ]),
                new("disgust.contempt", "Contempt", Children:
                [
                    new("disgust.contempt.scornful", "Scornful"),
                    new("disgust.contempt.disdainful", "Disdainful")
                ]),
                new("disgust.averse", "Averse", Children:
                [
                    new("disgust.averse.uncomfortable", "Uncomfortable"),
                    new("disgust.averse.avoidant", "Avoidant")
                ])
            ]),
        new(
            "surprise",
            "Surprise",
            "#2ED3C6",
            [
                new("surprise.startled", "Startled", Children:
                [
                    new("surprise.startled.jolted", "Jolted"),
                    new("surprise.startled.shaken", "Shaken")
                ]),
                new("surprise.confused", "Confused", Children:
                [
                    new("surprise.confused.puzzled", "Puzzled"),
                    new("surprise.confused.perplexed", "Perplexed")
                ]),
                new("surprise.amazed", "Amazed", Children:
                [
                    new("surprise.amazed.astonished", "Astonished"),
                    new("surprise.amazed.curious", "Curious")
                ]),
                new("surprise.shocked", "Shocked", Children:
                [
                    new("surprise.shocked.stunned", "Stunned"),
                    new("surprise.shocked.speechless", "Speechless")
                ])
            ]),
        new(
            "sadness",
            "Sadness",
            "#2D9CDB",
            [
                new("sadness.hurt", "Hurt", Children:
                [
                    new("sadness.hurt.wounded", "Wounded"),
                    new("sadness.hurt.rejected", "Rejected")
                ]),
                new("sadness.lonely", "Lonely", Children:
                [
                    new("sadness.lonely.isolated", "Isolated"),
                    new("sadness.lonely.abandoned", "Abandoned")
                ]),
                new("sadness.disappointed", "Disappointed", Children:
                [
                    new("sadness.disappointed.let-down", "Let down"),
                    new("sadness.disappointed.discouraged", "Discouraged")
                ]),
                new("sadness.grieving", "Grieving", Children:
                [
                    new("sadness.grieving.sorrowful", "Sorrowful"),
                    new("sadness.grieving.heartbroken", "Heartbroken")
                ]),
                new("sadness.despairing", "Despairing", Children:
                [
                    new("sadness.despairing.hopeless", "Hopeless"),
                    new("sadness.despairing.powerless", "Powerless")
                ]),
                new("sadness.regretful", "Regretful", Children:
                [
                    new("sadness.regretful.guilty", "Guilty"),
                    new("sadness.regretful.remorseful", "Remorseful")
                ]),
                new("sadness.exposed", "Exposed", Children:
                [
                    new("sadness.exposed.embarrassed", "Embarrassed"),
                    new("sadness.exposed.ashamed", "Ashamed")
                ]),
                new("sadness.fatigued", "Fatigued", Children:
                [
                    new("sadness.fatigued.tired", "Tired"),
                    new("sadness.fatigued.exhausted", "Exhausted")
                ]),
                new("sadness.numb", "Numb", Children:
                [
                    new("sadness.numb.empty", "Empty"),
                    new("sadness.numb.disconnected", "Disconnected")
                ])
            ])
    ];
}
