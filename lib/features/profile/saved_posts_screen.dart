import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/centered_content.dart';
import '../feed/post_card.dart';

class SavedPostsScreen extends ConsumerStatefulWidget {
  const SavedPostsScreen({super.key});

  @override
  ConsumerState<SavedPostsScreen> createState() => _SavedPostsScreenState();
}

class _SavedPostsScreenState extends ConsumerState<SavedPostsScreen> {
  @override
  void initState() {
    super.initState();
    Future.microtask(_load);
  }

  bool _isLoading = true;
  List<dynamic> _posts = [];
  String? _error;

  Future<void> _load() async {
    if (!mounted) return;
    setState(() => _isLoading = true);
    try {
      final posts = await ref.read(postRepositoryProvider).fetchSavedPosts();
      if (!mounted) return;
      setState(() {
        _posts = posts;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = 'Kaydedilenler yüklenemedi.';
        _isLoading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Kaydedilenler'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: _buildBody(),
      ),
    );
  }

  Widget _buildBody() {
    if (_isLoading) return const LoadingIndicator();
    if (_error != null) return ErrorView(message: _error!, onRetry: _load);
    if (_posts.isEmpty) {
      return const EmptyState(
        message: 'Henüz bir şey kaydetmedin.',
        icon: Icons.bookmark_border,
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: _posts.length,
      separatorBuilder: (_, __) => const SizedBox(height: 10),
      itemBuilder: (context, index) {
        final post = _posts[index];
        return PostCard(
          post: post,
          onTap: () => context.push('/posts/${post.id}', extra: post),
        );
      },
    );
  }
}

