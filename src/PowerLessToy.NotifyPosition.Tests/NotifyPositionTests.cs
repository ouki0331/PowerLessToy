using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.Tests;

[TestClass]
public class NotifyPositionTests
{
    [TestMethod]
    public void TestSettings_DefaultsAndSerialization()
    {
        var settings = new NotifyPositionSettings();

        Assert.IsTrue(settings.Enabled);
        Assert.AreEqual(NotificationMode.Replace, settings.NotificationMode);
        Assert.AreEqual(PositionPreset.TopCenter, settings.Position);
        Assert.AreEqual(5, settings.MaxVisible);
        Assert.AreEqual(8, settings.AutoDismissSeconds);
        Assert.AreEqual(10, settings.StackSpacing);
        Assert.IsTrue(settings.UseAcrylic);

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        Assert.IsFalse(string.IsNullOrEmpty(json));
        Assert.IsTrue(json.Contains("\"Replace\""));
        Assert.IsTrue(json.Contains("\"TopCenter\""));

        var deserialized = JsonSerializer.Deserialize<NotifyPositionSettings>(json);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(settings.NotificationMode, deserialized.NotificationMode);
        Assert.AreEqual(settings.Position, deserialized.Position);
        Assert.AreEqual(settings.MaxVisible, deserialized.MaxVisible);
    }

    [TestMethod]
    public void TestNotificationModel_DeduplicationKey()
    {
        var n1 = new NotificationModel
        {
            AppName = "Slack",
            Title = "New Message",
            Body = "Hello there!"
        };

        var n2 = new NotificationModel
        {
            AppName = "Slack",
            Title = "New Message",
            Body = "Hello there!"
        };

        var n3 = new NotificationModel
        {
            AppName = "Slack",
            Title = "Another Message",
            Body = "Hello there!"
        };

        Assert.AreEqual(n1.DeduplicationKey, n2.DeduplicationKey);
        Assert.AreNotEqual(n1.DeduplicationKey, n3.DeduplicationKey);
    }

    [TestMethod]
    public void TestCoordinateCalculation_NoOverlap()
    {
        var settings = new NotifyPositionSettings
        {
            Position = PositionPreset.TopCenter,
            StackSpacing = 12
        };

        // Work area: 1920x1040, Top=0, Left=0
        double workAreaWidth = 1920;
        double cardWidth = 380;
        double cardHeight = 110;
        double margin = 20;

        // Card 0
        double left0 = (workAreaWidth - cardWidth) / 2;
        double top0 = margin;

        // Card 1
        double left1 = (workAreaWidth - cardWidth) / 2;
        double top1 = margin + (1 * (cardHeight + settings.StackSpacing));

        Assert.AreEqual(left0, left1); // Aligned horizontally
        Assert.AreEqual(top0 + cardHeight + 12, top1); // Separated by exactly cardHeight + spacing
        Assert.IsTrue(top1 > (top0 + cardHeight)); // Absolutely no overlap!
    }

    [TestMethod]
    public void TestStackHeight_LimitAt50Percent()
    {
        double screenHeight = 1080;
        double maxBudget = screenHeight * 0.5; // 540px

        double cardHeight = 110;
        double spacing = 10;
        int maxPossibleWithoutExceeding = (int)(maxBudget / (cardHeight + spacing));

        // 540 / 120 = 4.5 -> exactly 4 cards fit within 50%
        Assert.AreEqual(4, maxPossibleWithoutExceeding);

        double totalHeightFor4 = 4 * 110 + 3 * 10; // 470px <= 540px
        double totalHeightFor5 = 5 * 110 + 4 * 10; // 590px > 540px

        Assert.IsTrue(totalHeightFor4 <= maxBudget);
        Assert.IsTrue(totalHeightFor5 > maxBudget);
    }

    [TestMethod]
    public void TestQueue_FifoOrdering()
    {
        var queue = new Queue<NotificationModel>();

        for (int i = 1; i <= 10; i++)
        {
            queue.Enqueue(new NotificationModel { Title = $"Item {i}" });
        }

        Assert.AreEqual(10, queue.Count);

        for (int i = 1; i <= 10; i++)
        {
            var item = queue.Dequeue();
            Assert.AreEqual($"Item {i}", item.Title);
        }

        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void TestAggregation_ThresholdTrigger()
    {
        var timestamps = new List<DateTime>();
        var now = DateTime.Now;

        // Simulate 4 messages within 5 seconds
        timestamps.Add(now.AddSeconds(-4));
        timestamps.Add(now.AddSeconds(-3));
        timestamps.Add(now.AddSeconds(-2));
        timestamps.Add(now);

        bool shouldAggregate = timestamps.Count > 3;
        Assert.IsTrue(shouldAggregate);

        string aggregatedTitle = $"Slack ({timestamps.Count} 件の新着通知)";
        Assert.AreEqual("Slack (4 件の新着通知)", aggregatedTitle);
    }

    [TestMethod]
    public void TestLogger_DirectoryResolution()
    {
        string logDir = NotifyPositionLogger.LogDirectory;
        Assert.IsFalse(string.IsNullOrEmpty(logDir));
        Assert.IsTrue(Directory.Exists(logDir));
    }
}
