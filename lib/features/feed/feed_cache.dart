import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../../shared/models/post.dart';

class FeedCache {
  const FeedCache();

  static const _prefix = 'feed_cache_v1';
  static const _maxPosts = 30;

  Future<List<Post>> read({
    required String sort,
    int? categoryId,
  }) async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_key(sort: sort, categoryId: categoryId));
    if (raw == null || raw.isEmpty) return const [];

    try {
      final json = jsonDecode(raw) as Map<String, Object?>;
      final items = json['posts'] as List<Object?>? ?? const [];
      return items
          .whereType<Map<String, Object?>>()
          .map(_postFromJson)
          .toList(growable: false);
    } catch (_) {
      await prefs.remove(_key(sort: sort, categoryId: categoryId));
      return const [];
    }
  }

  Future<void> write({
    required String sort,
    required int? categoryId,
    required List<Post> posts,
  }) async {
    final prefs = await SharedPreferences.getInstance();
    final capped = posts.take(_maxPosts).map(_postToJson).toList();
    await prefs.setString(
      _key(sort: sort, categoryId: categoryId),
      jsonEncode({
        'savedAt': DateTime.now().toIso8601String(),
        'posts': capped,
      }),
    );
  }

  static String _key({required String sort, required int? categoryId}) {
    final category = categoryId == null || categoryId == 0 ? 'all' : categoryId;
    return '$_prefix:$sort:$category';
  }

  static Map<String, Object?> _postToJson(Post post) => {
        'id': post.id,
        'category': {
          'id': post.category.id,
          'name': post.category.name,
          'icon': post.category.icon,
        },
        'title': post.title,
        'content': post.content,
        'createdAgo': post.createdAgo,
        'voteCountHakli': post.voteCountHakli,
        'voteCountHaksiz': post.voteCountHaksiz,
        'commentCount': post.commentCount,
        'createdAt': post.createdAt.toIso8601String(),
        'authorId': post.authorId,
        'authorName': post.authorName,
        'myVote': post.myVote?.name,
        'hasImage': post.hasImage,
        'isSaved': post.isSaved,
        'isEdited': post.isEdited,
        'isOwner': post.isOwner,
        'imageUrl': post.imageUrl,
        'status': post.status,
        'moderationReason': post.moderationReason,
        'createdOrder': post.createdOrder,
        'ranking_reason': post.rankingReason,
        'ranking_label': post.rankingLabel,
      };

  static Post _postFromJson(Map<String, Object?> json) {
    final category = json['category'] as Map<String, Object?>? ?? const {};
    final createdAt = DateTime.tryParse(json['createdAt'] as String? ?? '') ??
        DateTime.fromMillisecondsSinceEpoch(0);

    return Post(
      id: json['id'] as String? ?? '',
      category: Category(
        id: category['id'] as int? ?? 0,
        name: category['name'] as String? ?? 'Kategori',
        icon: category['icon'] as String? ?? '•',
      ),
      title: json['title'] as String? ?? '',
      content: json['content'] as String? ?? '',
      createdAgo: json['createdAgo'] as String? ?? '',
      voteCountHakli: json['voteCountHakli'] as int? ?? 0,
      voteCountHaksiz: json['voteCountHaksiz'] as int? ?? 0,
      commentCount: json['commentCount'] as int? ?? 0,
      comments: const [],
      createdAt: createdAt,
      authorId: json['authorId'] as String?,
      authorName: json['authorName'] as String?,
      myVote: _voteFromJson(json['myVote']),
      hasImage: json['hasImage'] as bool? ?? false,
      isSaved: json['isSaved'] as bool? ?? false,
      isEdited: json['isEdited'] as bool? ?? false,
      isOwner: json['isOwner'] as bool? ?? false,
      imageUrl: json['imageUrl'] as String?,
      status: json['status'] as String? ?? 'active',
      moderationReason: json['moderationReason'] as String?,
      createdOrder:
          json['createdOrder'] as int? ?? createdAt.millisecondsSinceEpoch,
      rankingReason: json['ranking_reason'] as String?,
      rankingLabel: json['ranking_label'] as String?,
    );
  }

  static VoteType? _voteFromJson(Object? value) => switch (value) {
        'hakli' => VoteType.hakli,
        'haksiz' => VoteType.haksiz,
        _ => null,
      };
}
