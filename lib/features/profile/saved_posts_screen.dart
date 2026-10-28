import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/centered_content.dart';
import 'widgets/column_app_bar.dart';
import 'widgets/verdict_tile.dart';

class SavedPostsScreen extends StatelessWidget {
  const SavedPostsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      appBar: ColumnAppBar(
        title: Text('Kaydedilenler'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: SavedPostsGrid(),
      ),
    );
  }
}

/// Saved posts as a grid of [VerdictTile]s (loads `fetchSavedPosts`).
///
/// With [sliver] the widget builds a sliver for an enclosing
/// `CustomScrollView` (profile "Kaydedilenler" tab); otherwise it is a
/// self-contained scroll view (the `/profile/saved` route).
class SavedPostsGrid extends ConsumerStatefulWidget {
  const SavedPostsGrid({super.key, this.sliver = false});

  final bool sliver;

  @override
  ConsumerState<SavedPostsGrid> createState() => _SavedPostsGridState();
}

class _SavedPostsGridState extends ConsumerState<SavedPostsGrid> {
  @override
  void initState() {
    super.initState();
    Future.microtask(_load);
  }

  bool _isLoading = true;
  List<Post> _posts = [];
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
    final sliver = _buildSliver(context);
    if (widget.sliver) return sliver;
    return CustomScrollView(
      slivers: [
        sliver,
        SliverToBoxAdapter(
          child: SizedBox(height: context.navBottomInset + 24),
        ),
      ],
    );
  }

  Widget _buildSliver(BuildContext context) {
    if (_isLoading) {
      return const SliverFillRemaining(
        hasScrollBody: false,
        child: Padding(
          padding: EdgeInsets.all(32),
          child: LoadingIndicator(),
        ),
      );
    }
    if (_error != null) {
      return SliverFillRemaining(
        hasScrollBody: false,
        child: ErrorView(message: _error!, onRetry: _load),
      );
    }
    if (_posts.isEmpty) {
      return const SliverFillRemaining(
        hasScrollBody: false,
        child: EmptyState(
          message: 'Henüz bir şey kaydetmedin.',
          icon: Icons.bookmark_border_rounded,
        ),
      );
    }

    return SliverPadding(
      padding: const EdgeInsets.all(16),
      sliver: SliverGrid(
        gridDelegate: VerdictTile.gridDelegate(context),
        delegate: SliverChildBuilderDelegate(
          (context, index) {
            final post = _posts[index];
            return VerdictTile(
              post: post,
              onTap: () => context.push('/posts/${post.id}', extra: post),
            );
          },
          childCount: _posts.length,
        ),
      ),
    );
  }
}
