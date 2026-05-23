import 'dart:math';

enum VoteType { hakli, haksiz }

class PollOption {
  const PollOption({
    required this.id,
    required this.text,
    required this.voteCount,
  });

  final String id;
  final String text;
  final int voteCount;

  PollOption copyWith({int? voteCount}) => PollOption(
        id: id,
        text: text,
        voteCount: voteCount ?? this.voteCount,
      );
}

class PostPoll {
  const PostPoll({
    required this.options,
    this.mySelectionId,
    this.totalVotes = 0,
  });

  final List<PollOption> options;
  final String? mySelectionId;
  final int totalVotes;

  bool get hasVoted => mySelectionId != null;

  PostPoll copyWith({
    List<PollOption>? options,
    String? mySelectionId,
    bool clearSelection = false,
    int? totalVotes,
  }) =>
      PostPoll(
        options: options ?? this.options,
        mySelectionId: clearSelection ? null : (mySelectionId ?? this.mySelectionId),
        totalVotes: totalVotes ?? this.totalVotes,
      );
}

class PostStats {
  const PostStats({
    required this.viewCount,
    required this.voteRate,
    required this.avgReadingSeconds,
    required this.voteTimeline,
  });

  final int viewCount;
  final int voteRate;
  final int avgReadingSeconds;
  final List<int> voteTimeline;
}

class TrendTopic {
  const TrendTopic({
    required this.name,
    required this.postCount,
    this.growthPercent,
  });

  final String name;
  final int postCount;
  final int? growthPercent;
}

class DiscoverData {
  const DiscoverData({
    required this.rising,
    required this.controversial,
    required this.fresh,
    this.cityTrending = const [],
    this.city,
    this.trendTopics = const [],
    this.todaysPosts = const [],
  });

  final List<Post> rising;
  final List<Post> controversial;
  final List<Post> fresh;
  final List<Post> cityTrending;
  final String? city;
  final List<TrendTopic> trendTopics;
  final List<Post> todaysPosts;
}

class Category {
  const Category({required this.id, required this.name, required this.icon});

  final int id;
  final String name;
  final String icon;
}

class Comment {
  const Comment({
    required this.id,
    required this.content,
    required this.upvoteCount,
    this.downvoteCount = 0,
    required this.createdAgo,
    this.authorId,
    this.authorName,
    this.parentId,
    this.replies = const [],
    this.myUpvote = false,
    this.myDownvote = false,
    this.isOwner = false,
    this.isEdited = false,
    this.isPinned = false,
    this.isRising = false,
    this.isPostOwner = false,
    this.reactions = const {},
    this.myReaction,
    this.upvotesHakli = 0,
    this.upvotesHaksiz = 0,
  });

  final String id;
  final String content;
  final int upvoteCount;
  final int downvoteCount;
  final String createdAgo;
  final String? authorId;
  final String? authorName;
  final String? parentId;
  final List<Comment> replies;
  final bool myUpvote;
  final bool myDownvote;
  final bool isOwner;
  final bool isEdited;
  final bool isPinned;
  final bool isRising;
  final bool isPostOwner;
  final Map<String, int> reactions;
  final String? myReaction;
  final int upvotesHakli;
  final int upvotesHaksiz;

  double get bridgingScore {
    if (upvotesHakli == 0 || upvotesHaksiz == 0) return 0.0;
    // Simple bridging formula: geometric mean or harmonic mean of group upvotes
    // Higher score if upvotes are balanced across disagreeing groups
    return sqrt(upvotesHakli * upvotesHaksiz).toDouble();
  }

  Comment copyWith({
    String? content,
    int? upvoteCount,
    int? downvoteCount,
    bool? myUpvote,
    bool? myDownvote,
    bool? isOwner,
    String? authorName,
    List<Comment>? replies,
    bool? isEdited,
    bool? isPinned,
    bool? isRising,
    bool? isPostOwner,
    Map<String, int>? reactions,
    String? myReaction,
    bool clearMyReaction = false,
    int? upvotesHakli,
    int? upvotesHaksiz,
  }) {
    return Comment(
      id: id,
      content: content ?? this.content,
      upvoteCount: upvoteCount ?? this.upvoteCount,
      downvoteCount: downvoteCount ?? this.downvoteCount,
      createdAgo: createdAgo,
      authorId: authorId,
      authorName: this.authorName,
      parentId: parentId,
      replies: replies ?? this.replies,
      myUpvote: myUpvote ?? this.myUpvote,
      myDownvote: myDownvote ?? this.myDownvote,
      isOwner: isOwner ?? this.isOwner,
      isEdited: isEdited ?? this.isEdited,
      isPinned: isPinned ?? this.isPinned,
      isRising: isRising ?? this.isRising,
      isPostOwner: isPostOwner ?? this.isPostOwner,
      reactions: reactions ?? this.reactions,
      myReaction: clearMyReaction ? null : (myReaction ?? this.myReaction),
      upvotesHakli: upvotesHakli ?? this.upvotesHakli,
      upvotesHaksiz: upvotesHaksiz ?? this.upvotesHaksiz,
    );
  }
}

class MyComment {
  const MyComment({
    required this.id,
    required this.content,
    required this.upvoteCount,
    this.downvoteCount = 0,
    required this.isEdited,
    required this.isPinned,
    required this.createdAgo,
    required this.postId,
    required this.postTitle,
  });

  final String id;
  final String content;
  final int upvoteCount;
  final int downvoteCount;
  final bool isEdited;
  final bool isPinned;
  final String createdAgo;
  final String postId;
  final String postTitle;
}

class KarmaHistory {
  const KarmaHistory({
    required this.id,
    required this.sourceType,
    required this.sourceId,
    required this.milestone,
    required this.karmaDelta,
    required this.createdAt,
    required this.createdAgo,
  });

  final String id;
  final String sourceType;
  final String sourceId;
  final int milestone;
  final int karmaDelta;
  final DateTime createdAt;
  final String createdAgo;
}

class WeeklyStats {
  const WeeklyStats({
    required this.weekLabel,
    required this.karmaEarned,
    required this.votesGiven,
    required this.hakliGiven,
    required this.haksizGiven,
    required this.postsCreated,
    required this.commentsPosted,
    required this.streak,
  });

  final String weekLabel;
  final int karmaEarned;
  final int votesGiven;
  final int hakliGiven;
  final int haksizGiven;
  final int postsCreated;
  final int commentsPosted;
  final int streak;
}

class Post {
  const Post({
    required this.id,
    required this.category,
    required this.title,
    required this.content,
    required this.createdAgo,
    required this.voteCountHakli,
    required this.voteCountHaksiz,
    required this.commentCount,
    required this.comments,
    required this.createdAt,
    this.authorId,
    this.authorName,
    this.myVote,
    this.hasImage = false,
    this.isSaved = false,
    this.isEdited = false,
    this.isOwner = false,
    this.imageUrl,
    this.imageUrls = const [],
    this.status = 'active',
    this.moderationReason,
    this.createdOrder = 0,
    this.perspectiveScore = 0.0,
    this.isClosed = false,
    this.tags = const [],
    this.aiSummary,
    this.poll,
    this.isUnlisted = false,
    this.rankingReason,
    this.rankingLabel,
  });

  final String id;
  final Category category;
  final String title;
  final String content;
  final String createdAgo;
  final int voteCountHakli;
  final int voteCountHaksiz;
  final int commentCount;
  final List<Comment> comments;
  final DateTime createdAt;
  final String? authorId;
  final String? authorName;
  final VoteType? myVote;
  final bool hasImage;
  final bool isSaved;
  final bool isEdited;
  final bool isOwner;
  final String? imageUrl;
  final List<String> imageUrls;
  final String status;
  final String? moderationReason;
  final int createdOrder;
  final double perspectiveScore;
  final bool isClosed;
  final List<String> tags;
  final String? aiSummary;
  final PostPoll? poll;
  final bool isUnlisted;
  final String? rankingReason;
  final String? rankingLabel;

  int get totalVotes => voteCountHakli + voteCountHaksiz;

  double get trendScore {
    final ageHours = DateTime.now().difference(createdAt).inMinutes / 60.0;
    return (totalVotes + (commentCount * 2)) / pow(ageHours + 2, 1.5);
  }

  double get ucbScore {
    // Simplified UCB for exploration (Phase 3)
    // Boosts posts with < 10 votes to ensure "fırsat eşitliği"
    if (totalVotes >= 10) return trendScore;
    final ageHours = DateTime.now().difference(createdAt).inMinutes / 60.0;
    if (ageHours > 24) return trendScore; // Don't boost old inactive posts

    // Exploration bonus: higher for younger posts with fewer votes
    final explorationBonus = 2.0 / (totalVotes + 1);
    return trendScore + explorationBonus;
  }

  bool get showPercentage => totalVotes >= 40;

  bool get canHaveAiSummary => totalVotes >= 50 && commentCount >= 5;

  int get readingTimeMinutes {
    final wordCount = content.trim().split(RegExp(r'\s+')).length;
    final time = (wordCount / 200).ceil();
    return time < 1 ? 1 : time;
  }

  bool get isSensitive => perspectiveScore > 0.4 || status == 'auto_hidden';

  int get hakliPercent {
    if (totalVotes == 0) {
      return 50;
    }
    return ((voteCountHakli / totalVotes) * 100).round();
  }

  Post copyWith({
    String? title,
    String? content,
    int? voteCountHakli,
    int? voteCountHaksiz,
    int? commentCount,
    DateTime? createdAt,
    VoteType? myVote,
    bool clearVote = false,
    List<Comment>? comments,
    bool? hasImage,
    bool? isSaved,
    bool? isEdited,
    bool? isOwner,
    String? authorName,
    String? imageUrl,
    List<String>? imageUrls,
    String? status,
    String? moderationReason,
    int? createdOrder,
    double? perspectiveScore,
    List<String>? tags,
    String? aiSummary,
    PostPoll? poll,
    bool? isUnlisted,
    String? rankingReason,
    String? rankingLabel,
  }) {
    return Post(
      id: id,
      category: category,
      title: title ?? this.title,
      content: content ?? this.content,
      createdAgo: createdAgo,
      voteCountHakli: voteCountHakli ?? this.voteCountHakli,
      voteCountHaksiz: voteCountHaksiz ?? this.voteCountHaksiz,
      commentCount: commentCount ?? this.commentCount,
      comments: comments ?? this.comments,
      createdAt: createdAt ?? this.createdAt,
      authorId: authorId,
      authorName: authorName ?? this.authorName,
      myVote: clearVote ? null : myVote ?? this.myVote,
      hasImage: hasImage ?? this.hasImage,
      isSaved: isSaved ?? this.isSaved,
      isEdited: isEdited ?? this.isEdited,
      isOwner: isOwner ?? this.isOwner,
      imageUrl: imageUrl ?? this.imageUrl,
      imageUrls: imageUrls ?? this.imageUrls,
      status: status ?? this.status,
      moderationReason: moderationReason ?? this.moderationReason,
      createdOrder: createdOrder ?? this.createdOrder,
      perspectiveScore: perspectiveScore ?? this.perspectiveScore,
      tags: tags ?? this.tags,
      aiSummary: aiSummary ?? this.aiSummary,
      poll: poll ?? this.poll,
      isUnlisted: isUnlisted ?? this.isUnlisted,
      rankingReason: rankingReason ?? this.rankingReason,
      rankingLabel: rankingLabel ?? this.rankingLabel,
    );
  }
}
