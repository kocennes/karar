import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminPostsScreen extends StatefulWidget {
  const AdminPostsScreen({super.key, required this.adminService});
  final AdminService adminService;

  @override
  State<AdminPostsScreen> createState() => _AdminPostsScreenState();
}

class _AdminPostsScreenState extends State<AdminPostsScreen> {
  List<AdminPost> _items = [];
  bool _loading = true;
  String? _error;
  final _searchCtrl = TextEditingController();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _searchCtrl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final q = _searchCtrl.text.trim();
      final result = await widget.adminService.fetchPosts(
          search: q.isEmpty ? null : q);
      setState(() => _items = result.items);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _delete(AdminPost post) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        backgroundColor: const Color(0xFF1E293B),
        title: const Text('Postu sil?', style: TextStyle(color: Colors.white)),
        content: Text(
          post.content.length > 80
              ? '${post.content.substring(0, 80)}...'
              : post.content,
          style: const TextStyle(color: Colors.white70),
        ),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('İptal',
                  style: TextStyle(color: Colors.white54))),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppColors.haksiz),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Sil'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      await widget.adminService.deletePost(post.id);
      setState(() => _items.remove(post));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      appBar: AppBar(
        backgroundColor: const Color(0xFF1E293B),
        title: const Text('Postlar', style: TextStyle(color: Colors.white)),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              controller: _searchCtrl,
              style: const TextStyle(color: Colors.white),
              decoration: InputDecoration(
                hintText: 'Post içeriğinde ara...',
                hintStyle: const TextStyle(color: Colors.white38),
                prefixIcon: const Icon(Icons.search, color: Colors.white38),
                filled: true,
                fillColor: const Color(0xFF1E293B),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(10),
                  borderSide: BorderSide.none,
                ),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.arrow_forward, color: Colors.white54),
                  onPressed: _load,
                ),
              ),
              onSubmitted: (_) => _load(),
            ),
          ),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(child: Text(_error!,
                        style: const TextStyle(color: Colors.white54)))
                    : _items.isEmpty
                        ? const Center(child: Text('Post bulunamadı.',
                            style: TextStyle(color: Colors.white54)))
                        : ListView.separated(
                            padding: const EdgeInsets.symmetric(horizontal: 16),
                            itemCount: _items.length,
                            separatorBuilder: (_, __) =>
                                const SizedBox(height: 8),
                            itemBuilder: (_, i) => _PostRow(
                              post: _items[i],
                              onDelete: () => _delete(_items[i]),
                            ),
                          ),
          ),
        ],
      ),
    );
  }
}

class _PostRow extends StatelessWidget {
  const _PostRow({required this.post, required this.onDelete});
  final AdminPost post;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.white.withValues(alpha: 0.07)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (post.username != null)
                  Text('@${post.username}',
                      style: const TextStyle(
                          color: AppColors.primary,
                          fontSize: 12,
                          fontWeight: FontWeight.w600)),
                const SizedBox(height: 4),
                Text(
                  post.content,
                  style: const TextStyle(color: Colors.white, fontSize: 13, height: 1.4),
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Text('${post.voteCount} oy',
                        style: const TextStyle(color: Colors.white38, fontSize: 11)),
                    if (post.reportCount > 0) ...[
                      const SizedBox(width: 10),
                      Text('${post.reportCount} rapor',
                          style: const TextStyle(
                              color: AppColors.haksiz, fontSize: 11)),
                    ],
                    const SizedBox(width: 10),
                    Text(
                      _formatDate(post.createdAt),
                      style: const TextStyle(color: Colors.white38, fontSize: 11),
                    ),
                  ],
                ),
              ],
            ),
          ),
          IconButton(
            icon: const Icon(Icons.delete_outline, color: AppColors.haksiz),
            onPressed: onDelete,
            tooltip: 'Sil',
          ),
        ],
      ),
    );
  }

  String _formatDate(DateTime d) {
    final diff = DateTime.now().difference(d);
    if (diff.inDays > 0) return '${diff.inDays}g önce';
    if (diff.inHours > 0) return '${diff.inHours}s önce';
    return '${diff.inMinutes}d önce';
  }
}
