import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminReportsScreen extends StatefulWidget {
  const AdminReportsScreen({super.key, required this.adminService});
  final AdminService adminService;

  @override
  State<AdminReportsScreen> createState() => _AdminReportsScreenState();
}

class _AdminReportsScreenState extends State<AdminReportsScreen> {
  List<AdminReport> _items = [];
  bool _loading = true;
  String? _error;
  String _status = 'pending';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final result = await widget.adminService.fetchReports(
        status: _status == 'all' ? null : _status,
      );
      setState(() => _items = result.items);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _action(AdminReport report, String action) async {
    try {
      await widget.adminService.reportAction(
          reportId: report.id, action: action);
      setState(() => _items.remove(report));
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('İşlem başarısız.')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      appBar: AppBar(
        backgroundColor: const Color(0xFF1E293B),
        title: const Text('Raporlar', style: TextStyle(color: Colors.white)),
        actions: [
          DropdownButton<String>(
            value: _status,
            dropdownColor: const Color(0xFF1E293B),
            style: const TextStyle(color: Colors.white),
            underline: const SizedBox(),
            items: const [
              DropdownMenuItem(value: 'pending', child: Text('Bekleyen')),
              DropdownMenuItem(value: 'actioned', child: Text('İşleme Alınan')),
              DropdownMenuItem(value: 'dismissed', child: Text('Reddedilen')),
              DropdownMenuItem(value: 'all', child: Text('Tümü')),
            ],
            onChanged: (v) {
              if (v != null) setState(() => _status = v);
              _load();
            },
          ),
          IconButton(
              icon: const Icon(Icons.refresh, color: Colors.white70),
              onPressed: _load),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? _center(_error!)
              : _items.isEmpty
                  ? const Center(
                      child: Text('Rapor yok.',
                          style: TextStyle(color: Colors.white54)))
                  : ListView.separated(
                      padding: const EdgeInsets.all(16),
                      itemCount: _items.length,
                      separatorBuilder: (_, __) => const SizedBox(height: 12),
                      itemBuilder: (_, i) => _ReportCard(
                        report: _items[i],
                        onAction: (a) => _action(_items[i], a),
                      ),
                    ),
    );
  }

  Widget _center(String msg) => Center(
      child: Text(msg, style: const TextStyle(color: Colors.white54)));
}

class _ReportCard extends StatelessWidget {
  const _ReportCard({required this.report, required this.onAction});
  final AdminReport report;
  final void Function(String) onAction;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.white.withValues(alpha: 0.07)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: AppColors.haksiz.withValues(alpha: 0.15),
                  borderRadius: BorderRadius.circular(4),
                ),
                child: Text(report.reason,
                    style: const TextStyle(
                        color: AppColors.haksiz,
                        fontSize: 11,
                        fontWeight: FontWeight.w700)),
              ),
              const SizedBox(width: 8),
              Text(report.targetType,
                  style: const TextStyle(color: Colors.white54, fontSize: 12)),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            report.content,
            style: const TextStyle(color: Colors.white, fontSize: 14, height: 1.4),
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              OutlinedButton(
                onPressed: () => onAction('actioned'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: AppColors.hakli,
                  side: BorderSide(color: AppColors.hakli.withValues(alpha: 0.5)),
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                  minimumSize: Size.zero,
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6)),
                ),
                child: const Text('İşleme Al',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
              ),
              const SizedBox(width: 8),
              OutlinedButton(
                onPressed: () => onAction('dismissed'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: Colors.white54,
                  side: BorderSide(color: Colors.white.withValues(alpha: 0.2)),
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                  minimumSize: Size.zero,
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6)),
                ),
                child: const Text('Reddet',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
