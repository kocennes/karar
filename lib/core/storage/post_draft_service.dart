import 'dart:convert';
import 'package:shared_preferences/shared_preferences.dart';

class PostDraft {
  const PostDraft({
    required this.title,
    required this.content,
    this.categoryId,
    this.tags = const [],
  });

  final String title;
  final String content;
  final int? categoryId;
  final List<String> tags;

  bool get isEmpty => title.isEmpty && content.isEmpty;

  Map<String, dynamic> toJson() => {
        'title': title,
        'content': content,
        'categoryId': categoryId,
        'tags': tags,
      };

  factory PostDraft.fromJson(Map<String, dynamic> json) => PostDraft(
        title: json['title'] as String? ?? '',
        content: json['content'] as String? ?? '',
        categoryId: json['categoryId'] as int?,
        tags: (json['tags'] as List<dynamic>?)
                ?.map((e) => e as String)
                .toList() ??
            const [],
      );
}

class PostDraftService {
  static const _key = 'post_draft_v1';

  Future<void> saveDraft(PostDraft draft) async {
    if (draft.isEmpty) {
      await clearDraft();
      return;
    }
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_key, jsonEncode(draft.toJson()));
  }

  Future<PostDraft?> loadDraft() async {
    final prefs = await SharedPreferences.getInstance();
    final json = prefs.getString(_key);
    if (json == null) return null;
    try {
      final draft = PostDraft.fromJson(jsonDecode(json) as Map<String, dynamic>);
      return draft.isEmpty ? null : draft;
    } catch (_) {
      return null;
    }
  }

  Future<void> clearDraft() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_key);
  }
}
