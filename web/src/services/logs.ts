import api from './api';
import type {
  AIRequestLogQueryRequest,
  AIRequestLogDto,
  PagedResponse,
  LogStatistics,
  HourlySummaryDto,
  HourlyByModelDto,
  HourlyByAccountDto,
  AggregatedStatistics,
} from '@/types/logs';

export const logsApi = {
  // 查询日志列表
  queryLogs: async (
    request: AIRequestLogQueryRequest
  ): Promise<PagedResponse<AIRequestLogDto>> => {
    return api.post<PagedResponse<AIRequestLogDto>>('/logs/query', request);
  },

  // 获取日志统计信息
  getStatistics: async (params?: {
    accountId?: number;
    startTime?: string;
    endTime?: string;
  }): Promise<LogStatistics> => {
    return api.get<LogStatistics>('/logs/statistics', params);
  },

  // 获取聚合统计信息（高性能版本）
  getAggregatedStatistics: async (params?: {
    startTime?: string;
    endTime?: string;
  }): Promise<AggregatedStatistics> => {
    return api.get<AggregatedStatistics>('/logs/statistics/aggregated', params);
  },

  // 获取每小时总体统计趋势
  getHourlySummary: async (params?: {
    startTime?: string;
    endTime?: string;
  }): Promise<HourlySummaryDto[]> => {
    return api.get<HourlySummaryDto[]>('/logs/hourly/summary', params);
  },

  // 获取按模型分组的小时统计
  getHourlyByModel: async (params?: {
    startTime?: string;
    endTime?: string;
    model?: string;
    provider?: string;
  }): Promise<HourlyByModelDto[]> => {
    return api.get<HourlyByModelDto[]>('/logs/hourly/by-model', params);
  },

  // 获取按账户分组的小时统计
  getHourlyByAccount: async (params?: {
    startTime?: string;
    endTime?: string;
    accountId?: number;
  }): Promise<HourlyByAccountDto[]> => {
    return api.get<HourlyByAccountDto[]>('/logs/hourly/by-account', params);
  },
};
