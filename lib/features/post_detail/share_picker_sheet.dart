import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:share_plus/share_plus.dart';

import '../../shared/models/post.dart';
import 'share_card_widget.dart';

class SharePickerSheet extends StatelessWidget {
  const SharePickerSheet({super.key, required this.post});

  final Post post;

  static Future<void> show(BuildContext context, Post post) {
    return showModalBottomSheet<void>(
      context: context,
      builder: (_) => SharePickerSheet(post: post),
    );
  }

  String get _postUrl => 'https://karar.app/posts/${post.id}';

  Future<void> _shareLink(BuildContext context) async {
    Navigator.pop(context);
    final total = post.totalVotes;
    String stats = '';
    if (total >= 10) {
      stats = '$total kişi oyladı · %${post.hakliPercent} Haklı · ';
    }
    await Share.share(
      '${post.title}\n\n${stats}Senin kararın ne? Karar ver: $_postUrl',
      subject: 'Karar: ${post.title}',
    );
  }

  Future<void> _copyLink(BuildContext context) async {
    try {
      await Clipboard.setData(ClipboardData(text: _postUrl));
      if (!context.mounted) return;
      Navigator.pop(context);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Link kopyalandı.')),
      );
    } catch (_) {
      // W29: clipboard permission denied — show selectable URL fallback
      if (!context.mounted) return;
      _showClipboardFallback(context);
    }
  }

  void _showClipboardFallback(BuildContext context) {
    final ctrl = TextEditingController(text: _postUrl);
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Linki Kopyala'),
        content: TextField(
          controller: ctrl,
          readOnly: true,
          autofocus: true,
          onTap: () => ctrl.selection =
              TextSelection(baseOffset: 0, extentOffset: ctrl.text.length),
          decoration: const InputDecoration(
            helperText: 'Linki seçip kopyalayabilirsin.',
            suffixIcon: Icon(Icons.copy_outlined),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Kapat'),
          ),
        ],
      ),
    ).whenComplete(ctrl.dispose);
  }

  void _showCardPreview(BuildContext context) {
    Navigator.pop(context);
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        fullscreenDialog: true,
        builder: (_) => _CardPreviewScreen(post: post),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: Theme.of(context).colorScheme.onSurfaceVariant.withValues(alpha: 0.4),
                borderRadius: BorderRadius.circular(2),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              'Nasıl Paylaşmak İstersin?',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 20),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                _OptionButton(
                  icon: Icons.link_rounded,
                  label: 'Link\nPaylaş',
                  onTap: () => _shareLink(context),
                ),
                const SizedBox(width: 16),
                _OptionButton(
                  icon: Icons.image_outlined,
                  label: 'Kart\nOluştur',
                  onTap: () => _showCardPreview(context),
                ),
              ],
            ),
            const SizedBox(height: 12),
            _OptionButton(
              icon: Icons.copy_rounded,
              label: 'Linki\nKopyala',
              onTap: () => _copyLink(context),
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
  }
}

class _OptionButton extends StatelessWidget {
  const _OptionButton({
    required this.icon,
    required this.label,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: 120,
        padding: const EdgeInsets.symmetric(vertical: 16),
        decoration: BoxDecoration(
          border: Border.all(
            color: Theme.of(context).colorScheme.outlineVariant,
          ),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(
          children: [
            Icon(icon, size: 28),
            const SizedBox(height: 8),
            Text(
              label,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
      ),
    );
  }
}

class _CardPreviewScreen extends StatefulWidget {
  const _CardPreviewScreen({required this.post});

  final Post post;

  @override
  State<_CardPreviewScreen> createState() => _CardPreviewScreenState();
}

class _CardPreviewScreenState extends State<_CardPreviewScreen> {
  final _repaintKey = GlobalKey();
  bool _sharing = false;

  Future<void> _share() async {
    setState(() => _sharing = true);
    try {
      final bytes = await captureShareCard(_repaintKey);
      if (bytes == null) return;
      await Share.shareXFiles(
        [XFile.fromData(Uint8List.fromList(bytes), name: 'karar.png', mimeType: 'image/png')],
        text: 'karar.app/posts/${widget.post.id}',
      );
    } finally {
      if (mounted) setState(() => _sharing = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Paylaşım Kartı'),
        actions: [
          if (_sharing)
            const Padding(
              padding: EdgeInsets.all(16),
              child: SizedBox(
                width: 20,
                height: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            )
          else
            TextButton.icon(
              onPressed: _share,
              icon: const Icon(Icons.share_rounded),
              label: const Text('Paylaş'),
            ),
        ],
      ),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: RepaintBoundary(
            key: _repaintKey,
            child: ShareCardWidget(post: widget.post),
          ),
        ),
      ),
    );
  }
}
