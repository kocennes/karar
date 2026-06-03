import 'package:share_plus/share_plus.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import '../analytics/analytics_service.dart';
import '../../shared/models/post.dart';

class ShareService {
  const ShareService({
    required this.analyticsService,
  });

  final AnalyticsService analyticsService;

  static const String _baseUrl = 'https://karar.app';

  Future<void> sharePost(Post post) async {
    final String url = '$_baseUrl/posts/${post.id}';
    final String text = _buildShareText(post, url);

    final ShareResult result;
    try {
      result = await Share.share(
        text,
        subject: 'Karar: ${post.title}',
      );
    } catch (_) {
      if (kIsWeb) {
        await Clipboard.setData(ClipboardData(text: url));
      }
      return;
    }

    if (result.status == ShareResultStatus.success) {
      await analyticsService.logPostShared(
        postId: post.id,
        category: post.category.name,
      );
    } else if (kIsWeb && result.status == ShareResultStatus.unavailable) {
      await Clipboard.setData(ClipboardData(text: url));
    }
  }

  String _buildShareText(Post post, String url) {
    final total = post.voteCountHakli + post.voteCountHaksiz;

    String stats = '';
    if (total >= 10) {
      stats = '$total kişi oyladı · %${post.hakliPercent} Haklı · ';
    }

    return '${post.title}\n\n${stats}Senin kararın ne? Karar ver: $url';
  }

  Future<void> shareApp() async {
    const String text =
        'Karar: Topluluk Yargılıyor. Anlaşmazlıklarını anonim paylaş, topluluk karar versin. Hemen indir: $_baseUrl';

    final ShareResult result;
    try {
      result = await Share.share(text);
    } catch (_) {
      if (kIsWeb) {
        await Clipboard.setData(const ClipboardData(text: _baseUrl));
      }
      return;
    }

    if (kIsWeb && result.status == ShareResultStatus.unavailable) {
      await Clipboard.setData(const ClipboardData(text: _baseUrl));
    }
  }
}
