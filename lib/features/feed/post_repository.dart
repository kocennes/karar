import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_endpoints.dart';
import '../../shared/models/post.dart';

class FeedResponse {
  const FeedResponse({
    required this.posts,
    required this.hasMore,
    this.rankingLabel,
  });

  final List<Post> posts;
  final bool hasMore;
  final String? rankingLabel;
}

class PostRepository {
  const PostRepository({required ApiClient apiClient}) : _apiClient = apiClient;

  final ApiClient _apiClient;

  Future<FeedResponse> fetchFeed({
    int page = 1,
    int limit = 20,
    int? categoryId,
    String sort = 'trending',
    String? afterId,
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/posts',
      query: {
        'page': '$page',
        'limit': '$limit',
        'categoryId': categoryId?.toString(),
        'sort': sort,
        if (afterId != null) 'afterId': afterId,
      },
    );
    final posts = _readPosts(json);
    final pagination = json['pagination'] as Map<String, Object?>?;
    final hasMore = pagination?['hasNext'] as bool? ?? posts.length >= limit;
    return FeedResponse(
      posts: posts,
      hasMore: hasMore,
      rankingLabel: json['rankingLabel'] as String?,
    );
  }

  Future<List<Post>> search(
    String query, {
    int page = 1,
    int limit = 20,
    int? categoryId,
    int? minVotes,
    DateTime? startDate,
    DateTime? endDate,
    String sort = 'relevance',
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/search',
      query: {
        'q': query,
        'page': '$page',
        'limit': '$limit',
        if (categoryId != null) 'categoryId': '$categoryId',
        if (minVotes != null) 'minVotes': '$minVotes',
        if (startDate != null) 'from': startDate.toIso8601String(),
        if (endDate != null) 'to': endDate.toIso8601String(),
        'sort': sort,
      },
    );
    return _readPosts(json);
  }

  Future<DiscoverFeedState> fetchDiscoverFeed({
    String? cursor,
    int limit = 10,
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.postsDiscoverFeed,
      query: {
        if (cursor != null) 'cursor': cursor,
        'limit': '$limit',
      },
    );
    final itemsRaw = json['items'] as List<Object?>? ?? [];
    return DiscoverFeedState(
      items: itemsRaw.cast<Map<String, Object?>>().map((item) {
        final postJson = item['post'] as Map<String, Object?>;
        return DiscoverFeedItem(
          post: _postFromJson(postJson),
          rankingReason: item['rankingReason'] as String? ?? 'trending',
          impressionToken: item['impressionToken'] as String? ?? '',
          seenBefore: item['seenBefore'] as bool? ?? false,
        );
      }).toList(),
      nextCursor: json['nextCursor'] as String?,
    );
  }

  Future<void> sendDiscoverEvent({
    required String postId,
    required String eventType,
    int? dwellSeconds,
    String? impressionToken,
    String? rankingReason,
  }) async {
    try {
      await _apiClient.postJson<void>(
        ApiEndpoints.postsDiscoverEvents,
        body: {
          'postId': postId,
          'eventType': eventType,
          if (dwellSeconds != null) 'dwellSeconds': dwellSeconds,
          if (impressionToken != null) 'impressionToken': impressionToken,
          if (rankingReason != null)
            'metadata': {'ranking_reason': rankingReason},
        },
      );
    } catch (_) {}
  }

  Future<DiscoverData> fetchDiscover() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.postsDiscover,
    );
    final topicsRaw = json['trendTopics'] as List<Object?>? ?? [];
    return DiscoverData(
      rising: _readPostList(json['rising']),
      controversial: _readPostList(json['controversial']),
      fresh: _readPostList(json['fresh']),
      cityTrending: _readPostList(json['cityTrending']),
      city: json['city'] as String?,
      serendipity: _readPostList(json['serendipity']),
      trendTopics: topicsRaw
          .cast<Map<String, Object?>>()
          .map((t) => TrendTopic(
                name: t['name'] as String,
                postCount: t['postCount'] as int? ?? 0,
                growthPercent: t['growthPercent'] as int?,
              ))
          .toList(),
      todaysPosts: _readPostList(json['todaysPosts']),
    );
  }

  Future<WeeklyStats> fetchWeeklyStats() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.userMeWeeklyStats,
    );
    return WeeklyStats(
      weekLabel: json['weekLabel'] as String? ?? '',
      karmaEarned: json['karmaEarned'] as int? ?? 0,
      votesGiven: json['votesGiven'] as int? ?? 0,
      hakliGiven: json['hakliGiven'] as int? ?? 0,
      haksizGiven: json['haksizGiven'] as int? ?? 0,
      postsCreated: json['postsCreated'] as int? ?? 0,
      commentsPosted: json['commentsPosted'] as int? ?? 0,
      streak: json['streak'] as int? ?? 0,
    );
  }

  Future<List<Post>> fetchTodaysPosts({int limit = 20}) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.postsToday,
      query: {'limit': '$limit'},
    );
    return _readPostList(json['posts']);
  }

  Future<Post?> fetchWeeklyFeatured() async {
    try {
      final json = await _apiClient.getJson<Map<String, Object?>>(
        ApiEndpoints.postsWeeklyFeatured,
      );
      return _postFromJson(json);
    } catch (_) {
      return null;
    }
  }

  Future<List<TrendTopic>> fetchTrendTopics() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.trendTopics,
    );
    final items = json['topics'] as List<Object?>? ?? [];
    return items
        .cast<Map<String, Object?>>()
        .map((t) => TrendTopic(
              name: t['name'] as String,
              postCount: t['postCount'] as int? ?? 0,
              growthPercent: t['growthPercent'] as int?,
            ))
        .toList();
  }

  Future<void> muteCategory(int categoryId) async {
    await _apiClient.postJson<void>('/api/v1/categories/$categoryId/mute');
  }

  Future<void> unmuteCategory(int categoryId) async {
    await _apiClient.deleteJson<void>('/api/v1/categories/$categoryId/mute');
  }

  Future<Post> fetchPost(String id) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/posts/$id',
    );
    return _postFromJson(json);
  }

  Future<void> recordView(
    String postId, {
    int? dwellSeconds,
    bool wasInteracted = false,
  }) async {
    try {
      await _apiClient.postJson<void>(
        ApiEndpoints.postView(postId),
        body: dwellSeconds == null
            ? null
            : {
                'dwellSeconds': dwellSeconds,
                'wasInteracted': wasInteracted,
              },
      );
    } catch (_) {}
  }

  Future<List<Category>> fetchCategories() async {
    final json = await _apiClient.getJson<Object?>('/api/v1/categories');
    final categories = switch (json) {
      {'categories': final List<Object?> items} => items,
      final List<Object?> items => items,
      _ => const <Object?>[],
    };

    return categories
        .cast<Map<String, Object?>>()
        .map((c) => Category(
              id: c['id'] as int,
              name: c['name'] as String,
              icon: c['emoji'] as String? ?? '•',
            ))
        .toList();
  }

  Future<void> createPost({
    required String title,
    required String content,
    required int categoryId,
    List<XFile>? images,
    List<String> tags = const [],
    List<String>? pollOptions,
    bool isUnlisted = false,
    bool isAnonymous = true,
    required bool acceptedTerms,
    required bool acceptedCommunityGuidelines,
  }) async {
    if (images != null && images.isNotEmpty) {
      final image = images.first;
      final bytes = await image.readAsBytes();
      final formDataMap = <String, dynamic>{
        'title': title,
        'content': content,
        'categoryId': categoryId.toString(),
        if (tags.isNotEmpty) 'tags': tags.join(','),
        if (pollOptions != null) 'pollOptions': pollOptions.join(','),
        'isUnlisted': isUnlisted.toString(),
        'isAnonymous': isAnonymous.toString(),
        'acceptedTerms': acceptedTerms.toString(),
        'acceptedCommunityGuidelines': acceptedCommunityGuidelines.toString(),
        'image_0': MultipartFile.fromBytes(
          bytes,
          filename: image.name.isEmpty ? 'image.jpg' : image.name,
        ),
      };

      final formData = FormData.fromMap(formDataMap);
      await _apiClient.postMultipart<void>('/api/v1/posts', formData);
    } else {
      await _apiClient.postJson<void>(
        '/api/v1/posts',
        body: {
          'title': title,
          'content': content,
          'categoryId': categoryId,
          if (tags.isNotEmpty) 'tags': tags,
          if (pollOptions != null) 'pollOptions': pollOptions,
          'isUnlisted': isUnlisted,
          'isAnonymous': isAnonymous,
          'acceptedTerms': acceptedTerms,
          'acceptedCommunityGuidelines': acceptedCommunityGuidelines,
        },
      );
    }
  }

  Future<Post> vote(String postId, VoteType voteType) async {
    await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/posts/$postId/vote',
      body: {'voteType': voteType.name},
    );
    return fetchPost(postId);
  }

  Future<Post> votePoll(String postId, String optionId) async {
    await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/posts/$postId/poll/vote',
      body: {'optionId': optionId},
    );
    return fetchPost(postId);
  }

  Future<Post> removeVote(String postId) async {
    await _apiClient.deleteJson<Map<String, Object?>>(
      '/api/v1/posts/$postId/vote',
    );
    return fetchPost(postId);
  }

  Future<void> deletePost(String postId) async {
    await _apiClient.deleteJson<void>('/api/v1/posts/$postId');
  }

  Future<void> updatePost(String postId, String title, String content) async {
    await _apiClient.putJson<void>(
      '/api/v1/posts/$postId',
      body: {'title': title, 'content': content},
    );
  }

  Future<Uint8List> fetchStoryImageBytes(String postId) =>
      _apiClient.getBytes(ApiEndpoints.postStoryImage(postId));

  Future<double> checkToxicity(String content) async {
    try {
      final json = await _apiClient.postJson<Map<String, Object?>>(
        ApiEndpoints.moderationCheck,
        body: {'content': content},
      );
      return (json['toxicityScore'] as num?)?.toDouble() ?? 0.0;
    } catch (_) {
      return 0.0;
    }
  }

  Future<Post> refreshAiSummary(String postId) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/posts/$postId/ai-summary',
    );
    return _postFromJson(json);
  }

  Future<List<Comment>> fetchComments(
    String postId, {
    String sort = 'top',
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/posts/$postId/comments',
      query: {'sort': sort},
    );
    final risingCommentId = json['risingCommentId'] as String?;
    final comments = json['comments'] as List<Object?>? ?? const [];
    return comments.cast<Map<String, Object?>>().map((c) {
      final comment = _commentFromJson(c);
      if (risingCommentId != null && comment.id == risingCommentId) {
        return comment.copyWith(isRising: true);
      }
      return comment;
    }).toList(growable: false);
  }

  Future<List<Post>> fetchMyPosts({int page = 1, String sort = 'new'}) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.userMePosts,
      query: {'page': '$page', 'sort': sort},
    );
    return _readPosts(json);
  }

  Future<List<Post>> fetchSavedPosts() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/users/me/saved',
    );
    return _readPosts(json);
  }

  Future<void> savePost(String postId) async {
    await _apiClient.postJson<void>('/api/v1/posts/$postId/save');
  }

  Future<void> unsavePost(String postId) async {
    await _apiClient.deleteJson<void>('/api/v1/posts/$postId/save');
  }

  Future<void> followCategory(int categoryId) async {
    await _apiClient.postJson<void>('/api/v1/categories/$categoryId/follow');
  }

  Future<void> unfollowCategory(int categoryId) async {
    await _apiClient.deleteJson<void>('/api/v1/categories/$categoryId/follow');
  }

  Future<Comment> createComment(String postId, String content,
      {String? parentId}) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/posts/$postId/comments',
      body: {
        'content': content,
        if (parentId != null) 'parentId': parentId,
      },
    );
    // Backend returns CommentMutationResponse (id, content, status, createdAt)
    // which lacks full comment fields — build a minimal Comment from it.
    final createdAt = DateTime.parse(json['createdAt'] as String);
    return Comment(
      id: json['id'].toString(),
      content: json['content'] as String,
      upvoteCount: 0,
      createdAgo: _relativeTime(createdAt),
      isOwner: true,
      parentId: parentId,
    );
  }

  Future<void> deleteComment(String commentId) async {
    await _apiClient.deleteJson<void>('/api/v1/comments/$commentId');
  }

  Future<void> updateComment(String commentId, String content) async {
    await _apiClient.putJson<void>(
      '/api/v1/comments/$commentId',
      body: {'content': content},
    );
  }

  Future<Comment> upvoteComment(Comment comment) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/comments/${comment.id}/upvote',
    );
    return comment.copyWith(
      upvoteCount: json['upvoteCount'] as int,
      myUpvote: json['myUpvote'] as bool,
      downvoteCount: json['downvoteCount'] as int? ?? comment.downvoteCount,
      myDownvote: json['myDownvote'] as bool? ?? false,
    );
  }

  Future<Comment> removeCommentUpvote(Comment comment) async {
    final json = await _apiClient.deleteJson<Map<String, Object?>>(
      '/api/v1/comments/${comment.id}/upvote',
    );
    return comment.copyWith(
      upvoteCount: json['upvoteCount'] as int,
      myUpvote: json['myUpvote'] as bool,
      downvoteCount: json['downvoteCount'] as int? ?? comment.downvoteCount,
      myDownvote: json['myDownvote'] as bool? ?? false,
    );
  }

  Future<Comment> downvoteComment(Comment comment) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/comments/${comment.id}/downvote',
    );
    return comment.copyWith(
      downvoteCount: json['downvoteCount'] as int,
      myDownvote: json['myDownvote'] as bool,
      upvoteCount: json['upvoteCount'] as int? ?? comment.upvoteCount,
      myUpvote: json['myUpvote'] as bool? ?? false,
    );
  }

  Future<Comment> removeCommentDownvote(Comment comment) async {
    final json = await _apiClient.deleteJson<Map<String, Object?>>(
      '/api/v1/comments/${comment.id}/downvote',
    );
    return comment.copyWith(
      downvoteCount: json['downvoteCount'] as int,
      myDownvote: json['myDownvote'] as bool,
      upvoteCount: json['upvoteCount'] as int? ?? comment.upvoteCount,
      myUpvote: json['myUpvote'] as bool? ?? false,
    );
  }

  Future<Comment> reactToComment(String commentId, String emoji) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/comments/$commentId/reactions',
      body: {'emoji': emoji},
    );
    return _commentFromJson(json);
  }

  Future<Comment> removeCommentReaction(String commentId) async {
    final json = await _apiClient.deleteJson<Map<String, Object?>>(
      '/api/v1/comments/$commentId/reactions',
    );
    return _commentFromJson(json);
  }

  Future<List<Post>> fetchUserPosts(
    String username, {
    int page = 1,
    String sort = 'new',
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.userPosts(username),
      query: {'page': '$page', 'sort': sort},
    );
    return _readPosts(json);
  }

  Future<List<MyComment>> fetchUserComments(
    String username, {
    int page = 1,
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/users/$username/comments',
      query: {'page': '$page'},
    );
    final items = json['comments'] as List<Object?>? ?? const [];
    return items.cast<Map<String, Object?>>().map(_myCommentFromJson).toList();
  }

  Future<List<MyComment>> fetchMyComments({int page = 1}) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.userMeComments,
      query: {'page': '$page'},
    );
    final items = json['comments'] as List<Object?>? ?? const [];
    return items.cast<Map<String, Object?>>().map(_myCommentFromJson).toList();
  }

  Future<List<KarmaHistory>> fetchKarmaHistory({int page = 1}) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/users/me/karma-history',
      query: {'page': '$page'},
    );
    final items = json['items'] as List<Object?>? ?? const [];
    return items.cast<Map<String, Object?>>().map((item) {
      final createdAt = DateTime.parse(item['createdAt'] as String);
      return KarmaHistory(
        id: item['id'] as String,
        sourceType: item['sourceType'] as String,
        sourceId: item['sourceId'] as String,
        milestone: item['milestone'] as int,
        karmaDelta: item['karmaDelta'] as int,
        createdAt: createdAt,
        createdAgo: _relativeTime(createdAt),
      );
    }).toList();
  }

  Future<void> pinComment(String postId, String commentId) async {
    await _apiClient.postJson<void>(
      ApiEndpoints.postPinComment(postId),
      body: {'commentId': commentId},
    );
  }

  Future<void> unpinComment(String postId) async {
    await _apiClient.deleteJson<void>(ApiEndpoints.postPinComment(postId));
  }

  MyComment _myCommentFromJson(Map<String, Object?> json) {
    final createdAt = DateTime.parse(json['createdAt'] as String);
    return MyComment(
      id: json['id'] as String,
      content: json['content'] as String,
      upvoteCount: json['upvoteCount'] as int,
      downvoteCount: json['downvoteCount'] as int? ?? 0,
      isEdited: json['isEdited'] as bool? ?? false,
      isPinned: json['isPinned'] as bool? ?? false,
      createdAgo: _relativeTime(createdAt),
      postId: json['postId'] as String,
      postTitle: json['postTitle'] as String,
    );
  }

  Stream<SseEvent> watchPost(String postId) =>
      _apiClient.sseStream('/api/v1/posts/$postId/events');

  Future<List<Map<String, Object?>>> searchUsers(String query) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/search/users',
      query: {'q': query, 'limit': '20'},
    );
    final items = json['users'] as List<Object?>? ?? const [];
    return items.cast<Map<String, Object?>>().toList();
  }

  Future<List<Post>> fetchSimilarPosts(String postId) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.postSimilar(postId),
    );
    return _readPostList(json['posts']);
  }

  Future<PostStats> fetchPostStats(String postId) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.postStats(postId),
    );
    final timeline =
        (json['voteTimeline'] as List<Object?>? ?? []).cast<int>().toList();
    return PostStats(
      viewCount: json['viewCount'] as int? ?? 0,
      voteRate: json['voteRate'] as int? ?? 0,
      avgReadingSeconds: json['avgReadingSeconds'] as int? ?? 0,
      voteTimeline: timeline,
    );
  }

  Future<void> markNotInterested(String postId,
      {String reason = 'not_interested'}) async {
    await _apiClient.postJson<void>(
      '/api/v1/posts/$postId/feedback',
      body: {'type': reason},
    );
  }

  Future<void> report({
    required String targetType,
    required String targetId,
    required String reason,
    String? description,
  }) async {
    await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/reports',
      body: {
        'targetType': targetType,
        'targetId': targetId,
        'reason': reason,
        ...(description == null
            ? const <String, Object?>{}
            : {'description': description}),
      },
    );
  }

  List<Post> _readPosts(Map<String, Object?> json) {
    final posts = json['posts'] as List<Object?>? ?? const [];
    return posts
        .cast<Map<String, Object?>>()
        .map(_postFromJson)
        .toList(growable: false);
  }

  List<Post> _readPostList(Object? value) {
    final posts = value as List<Object?>? ?? const [];
    return posts
        .cast<Map<String, Object?>>()
        .map(_postFromJson)
        .toList(growable: false);
  }

  Post _postFromJson(Map<String, Object?> json) {
    final category = json['category'] as Map<String, Object?>;
    final createdAt = DateTime.parse(json['createdAt'] as String);
    return Post(
      id: json['id'] as String,
      category: Category(
        id: category['id'] as int,
        name: category['name'] as String,
        icon: category['emoji'] as String,
      ),
      title: json['title'] as String,
      content: json['content'] as String? ?? '',
      createdAgo: _relativeTime(createdAt),
      voteCountHakli: json['voteCountHakli'] as int,
      voteCountHaksiz: json['voteCountHaksiz'] as int,
      commentCount: json['commentCount'] as int,
      comments: const [],
      createdAt: createdAt,
      authorId: json['authorId'] as String? ?? json['userId'] as String?,
      authorName: json['authorName'] as String? ?? json['username'] as String?,
      myVote: _voteFromJson(json['myVote']),
      hasImage: json['imageUrl'] != null ||
          (json['imageUrls'] as List?)?.isNotEmpty == true,
      isSaved: json['isSaved'] as bool? ?? false,
      isEdited: json['isEdited'] as bool? ?? false,
      isOwner: json['isOwner'] as bool? ?? false,
      imageUrl: json['imageUrl'] as String?,
      imageUrls: (json['imageUrls'] as List<Object?>? ?? [])
          .whereType<String>()
          .toList(),
      status: json['status'] as String? ?? 'active',
      moderationReason: json['moderationReason'] as String?,
      createdOrder: createdAt.millisecondsSinceEpoch,
      perspectiveScore: (json['perspectiveScore'] as num?)?.toDouble() ?? 0.0,
      isClosed: json['isClosed'] as bool? ?? false,
      tags: (json['tags'] as List<Object?>? ?? []).whereType<String>().toList(),
      aiSummary: json['aiSummary'] as String?,
      poll: json['poll'] != null
          ? _pollFromJson(json['poll'] as Map<String, Object?>)
          : null,
      isUnlisted: json['isUnlisted'] as bool? ?? false,
      isAnonymous: json['isAnonymous'] as bool? ?? false,
      rankingReason: json['ranking_reason'] as String?,
      rankingLabel: json['ranking_label'] as String?,
    );
  }

  PostPoll _pollFromJson(Map<String, Object?> json) {
    final options = (json['options'] as List<Object?>? ?? [])
        .cast<Map<String, Object?>>()
        .map((o) => PollOption(
              id: o['id'] as String,
              text: o['text'] as String,
              voteCount: o['voteCount'] as int? ?? 0,
            ))
        .toList();
    return PostPoll(
      options: options,
      mySelectionId: json['mySelectionId'] as String?,
      totalVotes: json['totalVotes'] as int? ?? 0,
    );
  }

  Comment _commentFromJson(Map<String, Object?> json) {
    final createdAt = DateTime.parse(json['createdAt'] as String);
    final replies = json['replies'] as List<Object?>? ?? const [];
    return Comment(
      id: json['id'] as String,
      content: json['content'] as String,
      upvoteCount: json['upvoteCount'] as int,
      downvoteCount: json['downvoteCount'] as int? ?? 0,
      createdAgo: _relativeTime(createdAt),
      authorId: json['authorId'] as String? ?? json['userId'] as String?,
      authorName: json['authorName'] as String? ?? json['username'] as String?,
      parentId: json['parentId'] as String?,
      replies:
          replies.cast<Map<String, Object?>>().map(_commentFromJson).toList(),
      myUpvote: json['myUpvote'] as bool? ?? false,
      myDownvote: json['myDownvote'] as bool? ?? false,
      isOwner: json['isOwner'] as bool? ?? false,
      isEdited: json['isEdited'] as bool? ?? false,
      isPinned: json['isPinned'] as bool? ?? false,
      isPostOwner: json['isPostOwner'] as bool? ?? false,
      reactions: (json['reactions'] as Map<String, Object?>? ?? {})
          .map((k, v) => MapEntry(k, v as int)),
      myReaction: json['myReaction'] as String?,
      upvotesHakli: json['upvotesHakli'] as int? ?? 0,
      upvotesHaksiz: json['upvotesHaksiz'] as int? ?? 0,
    );
  }

  VoteType? _voteFromJson(Object? value) {
    return switch (value) {
      'hakli' => VoteType.hakli,
      'haksiz' => VoteType.haksiz,
      _ => null,
    };
  }

  String _relativeTime(DateTime createdAt) {
    final diff = DateTime.now().difference(createdAt.toLocal());
    if (diff.inMinutes < 1) {
      return 'şimdi';
    }
    if (diff.inHours < 1) {
      return '${diff.inMinutes}dk önce';
    }
    if (diff.inDays < 1) {
      return '${diff.inHours}s önce';
    }
    return '${diff.inDays}g önce';
  }
}
