import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:share_plus/share_plus.dart';

import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_sheet.dart';
import 'share_card_widget.dart';

class SharePickerSheet extends ConsumerStatefulWidget {
  const SharePickerSheet({super.key, required this.post});

  final Post post;

  static Future<void> show(BuildContext context, Post post) {
    return showKararSheet<void>(
      context,
      builder: (_) => SharePickerSheet(post: post),
    );
  }

  @override
  ConsumerState<SharePickerSheet> createState() => _SharePickerSheetState();
}

class _SharePickerSheetState extends ConsumerState<SharePickerSheet> {
  bool _storyLoading = false;

  String get _postUrl => 'https://karar.app/posts/${widget.post.id}?ref=share';

  Future<void> _shareLink(BuildContext context) async {
    final total = widget.post.totalVotes;
    String stats = '';
    if (total >= 10) {
      stats = '$total kişi oyladı · %${widget.post.hakliPercent} Haklı · ';
    }
    try {
      final result = await Share.share(
        '${widget.post.title}\n\n${stats}Senin kararın ne? Karar ver: $_postUrl',
        subject: 'Karar: ${widget.post.title}',
      );
      if (kIsWeb && result.status == ShareResultStatus.unavailable) {
        await _copyShareLinkFallback(context);
        return;
      }
      if (!context.mounted) return;
      Navigator.pop(context);
    } catch (_) {
      if (!kIsWeb) return;
      await _copyShareLinkFallback(context);
    }
  }

  Future<void> _copyShareLinkFallback(BuildContext context) async {
    try {
      await Clipboard.setData(ClipboardData(text: _postUrl));
      if (!context.mounted) return;
      Navigator.pop(context);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Link kopyalandi.')),
      );
    } catch (_) {
      if (!context.mounted) return;
      _showClipboardFallback(context);
    }
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
        builder: (_) => _CardPreviewScreen(post: widget.post),
      ),
    );
  }

  Future<void> _shareStory(BuildContext context) async {
    // Web'de story paylaşımı desteklenmez
    if (kIsWeb) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
            content: Text('Hikaye paylaşımı mobil uygulamada kullanılabilir.')),
      );
      return;
    }
    setState(() => _storyLoading = true);
    try {
      final bytes = await ref
          .read(postRepositoryProvider)
          .fetchStoryImageBytes(widget.post.id);
      if (!context.mounted) return;
      Navigator.pop(context);
      await Share.shareXFiles(
        [
          XFile.fromData(
            bytes,
            name: 'karar_story.png',
            mimeType: 'image/png',
          ),
        ],
        text: _postUrl,
      );
    } catch (_) {
      if (!context.mounted) return;
      setState(() => _storyLoading = false);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Hikaye görseli yüklenemedi.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.only(bottom: 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            KararSheetHeader(
              title: 'Nasıl Paylaşmak İstersin?',
              onClose: () => Navigator.pop(context),
            ),
            const SizedBox(height: 12),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: _OptionButton(
                      icon: Icons.link_rounded,
                      label: 'Link\nPaylaş',
                      onTap: () => _shareLink(context),
                    ),
                  ),
                  Expanded(
                    child: _OptionButton(
                      icon: Icons.image_outlined,
                      label: 'Kart\nOluştur',
                      onTap: () => _showCardPreview(context),
                    ),
                  ),
                  Expanded(
                    child: _OptionButton(
                      icon: Icons.auto_stories_outlined,
                      label: 'Hikaye\nPaylaş',
                      onTap: _storyLoading ? null : () => _shareStory(context),
                      loading: _storyLoading,
                    ),
                  ),
                  Expanded(
                    child: _OptionButton(
                      icon: Icons.copy_rounded,
                      label: 'Linki\nKopyala',
                      onTap: () => _copyLink(context),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Share target: 56 px circle + two-line 12/700 label. The circle is muted
/// in light and `raised` in dark (muted is only 1.08:1 off the dark sheet).
class _OptionButton extends StatelessWidget {
  const _OptionButton({
    required this.icon,
    required this.label,
    required this.onTap,
    this.loading = false,
  });

  final IconData icon;
  final String label;
  final VoidCallback? onTap;
  final bool loading;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return KararPressable(
      onTap: onTap,
      semanticLabel: label.replaceAll('\n', ' '),
      focusRadius: KararRadius.tile,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 6),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 56,
              height: 56,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: kc.isDark ? kc.raised : kc.muted,
                shape: BoxShape.circle,
              ),
              child: loading
                  ? SizedBox(
                      width: 24,
                      height: 24,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: kc.primary,
                      ),
                    )
                  : Icon(icon, size: 24, color: kc.text1),
            ),
            const SizedBox(height: 8),
            Text(
              label,
              textAlign: TextAlign.center,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.meta.copyWith(
                fontWeight: FontWeight.w700,
                letterSpacing: 0,
                color: kc.text1,
              ),
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
        [
          XFile.fromData(Uint8List.fromList(bytes),
              name: 'karar.png', mimeType: 'image/png')
        ],
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
              padding: EdgeInsets.all(12),
              child: SizedBox(
                width: 20,
                height: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            )
          else
            TextButton.icon(
              onPressed: _share,
              icon: const Icon(Icons.send_rounded, size: 20),
              label: const Text('Paylaş'),
            ),
        ],
      ),
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: FittedBox(
            fit: BoxFit.scaleDown,
            child: RepaintBoundary(
              key: _repaintKey,
              child: ShareCardWidget(post: widget.post),
            ),
          ),
        ),
      ),
    );
  }
}
